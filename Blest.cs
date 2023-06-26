#nullable enable
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Net.Http;
using System.Linq;

namespace Blest
{

    public class HttpServer
    {
        private readonly RequestHandler requestHandler;
        private readonly Dictionary<string, object?>? options;
        private readonly string[] allowedMethods = { "POST", "OPTIONS" };

        public HttpServer(RequestHandler requestHandler, Dictionary<string, object?>? options = null)
        {
            this.requestHandler = requestHandler;
            this.options = options;
        }

        public async Task Listen(string url)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(url);
            listener.Start();

            while (true)
            {
                HttpListenerContext context = await listener.GetContextAsync();
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", string.Join(", ", allowedMethods));

                if (request.Url != null && request.Url.AbsolutePath == "/")
                {
                    if ("POST".Equals(request.HttpMethod, StringComparison.OrdinalIgnoreCase))
                    {
                        using (StreamReader reader = new StreamReader(request.InputStream))
                        {
                            string body = await reader.ReadToEndAsync();

                            List<object> jsonData;
                            try
                            {
                                JsonElement jsonElement = JsonSerializer.Deserialize<JsonElement>(body);
                                if (jsonElement.ValueKind != JsonValueKind.Array)
                                {
                                    throw new JsonException("Request body should be a JSON array");
                                }
                                object? jsonInCSharp = ConvertJsonToCSharp(jsonElement);
                                if (jsonInCSharp != null)
                                {
                                    jsonData = (List<object>)jsonInCSharp;
                                }
                                else
                                {
                                    throw new JsonException("Request body should be valid JSON");
                                }
                            }
                            catch (JsonException error)
                            {
                                Console.Error.WriteLine(error);
                                SendErrorResponse(response, HttpStatusCode.BadRequest, error.Message);
                                continue;
                            }

                            try
                            {
                                var contextData = new Dictionary<string, object?>
                                {
                                    { "headers", request.Headers }
                                };

                                object?[] resultError = await requestHandler.Handle(jsonData, contextData);
                                dynamic? result = resultError[0];
                                dynamic? error = resultError[1];

                                if (error != null)
                                {
                                    SendErrorResponse(response, HttpStatusCode.InternalServerError, error["message"]);
                                }
                                else if (result != null)
                                {
                                    SendJsonResponse(response, HttpStatusCode.OK, result);
                                }
                                else
                                {
                                    SendNoContentResponse(response);
                                }
                            }
                            catch (Exception error)
                            {
                                Console.Error.WriteLine(error);
                                SendErrorResponse(response, HttpStatusCode.InternalServerError, error.Message);
                            }
                        }
                    }
                    else
                    {
                        SendErrorResponse(response, HttpStatusCode.MethodNotAllowed);
                    }
                }
                else if ("OPTIONS".Equals(request.HttpMethod, StringComparison.OrdinalIgnoreCase))
                {
                    SendNoContentResponse(response);
                }
                else
                {
                    SendErrorResponse(response, HttpStatusCode.NotFound);
                }

                response.OutputStream.Close();
            }
        }
        
        private static object ConvertJsonToCSharp(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var dictionary = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        dictionary[property.Name] = ConvertJsonToCSharp(property.Value);
                    }
                    return dictionary;

                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray())
                    {
                        list.Add(ConvertJsonToCSharp(item));
                    }
                    return list;

                case JsonValueKind.String:
                    return element.GetString()!;

                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                    {
                        return intValue;
                    }
                    if (element.TryGetInt64(out long longValue))
                    {
                        return longValue;
                    }
                    if (element.TryGetDouble(out double doubleValue))
                    {
                        return doubleValue;
                    }
                    return 0;

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                    return null!;

                default:
                    throw new NotSupportedException("Unsupported JSON value kind: " + element.ValueKind);
            }
        }

        private void SendErrorResponse(HttpListenerResponse response, HttpStatusCode statusCode, string? message = null)
        {
            response.StatusCode = (int)statusCode;

            if (!string.IsNullOrEmpty(message))
            {
                byte[] errorMessage = Encoding.UTF8.GetBytes(message);
                response.ContentLength64 = errorMessage.Length;
                response.OutputStream.Write(errorMessage, 0, errorMessage.Length);
            }
        }

        private void SendJsonResponse(HttpListenerResponse response, HttpStatusCode statusCode, dynamic data)
        {
            string jsonResponse = JsonSerializer.Serialize(data);
            byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
            response.StatusCode = (int)statusCode;
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        private void SendNoContentResponse(HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.NoContent;
        }
    }
    
    public class HttpClient
    {
        private readonly string url;
        private readonly int maxBatchSize = 100;
        private readonly List<object?[]> queue = new List<object?[]>();
        private Timer? timeout;
        private readonly System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();
        private readonly Dictionary<string, TaskCompletionSource<object>> pendingRequests = new Dictionary<string, TaskCompletionSource<object>>();

        public HttpClient(string url)
        {
            this.url = url;
        }

        public async Task<object> Request(string route, IDictionary<string, object?>? parameters = null, IList<object?>? selector = null)
        {
            if (string.IsNullOrEmpty(route))
            {
                throw new ArgumentException("Route is required");
            }

            if (parameters != null && !(parameters is IDictionary<string, object?>))
            {
                throw new ArgumentException("Params should be a dictionary");
            }

            if (selector != null && !(selector is object[]))
            {
                throw new ArgumentException("Selector should be a list");
            }

            string id = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<object>();

            lock (pendingRequests)
            {
                pendingRequests[id] = tcs;
            }

            var item = new object?[] { id, route, parameters, selector };
            queue.Add(item);

            if (timeout == null)
            {
                timeout = new Timer(Process, null, 1, Timeout.Infinite);
            }

            return await tcs.Task;
        }

        private async void Process(object? state)
        {
            object?[][] newQueue;
            lock (queue)
            {
                newQueue = queue.Count <= maxBatchSize ? queue.ToArray() : queue.GetRange(0, maxBatchSize).ToArray();
                queue.RemoveRange(0, Math.Min(queue.Count, maxBatchSize));
                timeout?.Dispose();
                timeout = null;
                if (queue.Count > 0)
                {
                    timeout = new Timer(Process, null, 1, Timeout.Infinite);
                }
            }

            if (newQueue.Length > 0)
            {
                var json = JsonSerializer.Serialize(newQueue);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    var response = await client.PostAsync(url, content);
                    response.EnsureSuccessStatusCode();
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var responseData = JsonSerializer.Deserialize<object[][]>(responseJson);
                    if (responseData == null)
                    {
                        throw new Exception("Server did not return anything");
                    }

                    foreach (var item in responseData)
                    {
                        var id = item[0].ToString();
                        var result = item.Length > 2 ? item[2] : null;
                        var error = item.Length > 3 && item[3] != null ? new Exception(item[3].ToString()) : null;

                        if (id == null || id == "")
                        {
                            throw new Exception();
                        }

                        TaskCompletionSource<object>? tcs;
                        lock (pendingRequests)
                        {
                            tcs = pendingRequests.GetValueOrDefault(id);
                            if (tcs != null)
                            {
                                pendingRequests.Remove(id);
                            }
                        }

                        if (tcs != null)
                        {
                            if (error != null)
                            {
                                tcs.SetException(error);
                            }
                            else if (result != null)
                            {
                                tcs.SetResult(result);
                            }
                            else
                            {
                                tcs.SetException(new Exception("No content returned"));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    foreach (var item in newQueue)
                    {
                        var id = (string)item[0]!;

                        TaskCompletionSource<object>? tcs;
                        lock (pendingRequests)
                        {
                            tcs = pendingRequests.GetValueOrDefault(id);
                            if (tcs != null)
                            {
                                pendingRequests.Remove(id);
                            }
                        }

                        if (tcs != null)
                        {
                            tcs.SetException(ex);
                        }
                    }
                }
            }
        }
    }

    public class RequestHandler
    {
        private readonly Dictionary<string, List<Delegate>> routes;
        private readonly Dictionary<string, object?>? options;
        private readonly string routeRegex = @"^[a-zA-Z][a-zA-Z0-9_\-\/]*[a-zA-Z0-9_\-]$";

        public RequestHandler(Dictionary<string, List<Delegate>> routes, Dictionary<string, object?>? options = null)
        {
            this.routes = routes;
            this.options = options;
        }

        public async Task<object?[]> Handle(List<object> requests, Dictionary<string, object?>? context)
        {
            if (requests == null || requests.GetType() != typeof(List<object>))
            {
                return HandleError(400, "Request body should be a JSON array");
            }
            if (!requests.All(r => r is List<object>)) {
                return HandleError(400, "Request item should be a JSON array");
            }

            if (context == null)
            {
                context = new Dictionary<string, object?>();
            }

            List<string> uniqueIds = new List<string>();
            List<Task<object?[]>> tasks = new List<Task<object?[]>>();

            for (int i = 0; i < requests.Count; i++)
            {
                IList<object?> request;

                try
                {
                    request = (IList<object?>)requests[i];
                }
                catch (Exception error)
                {
                    return HandleError(400, error.Message);
                }

                int requestLength = request.Count;

                string? id = requestLength > 0 ? (string?)request[0] : null;
                string? route = requestLength > 1 ? (string?)request[1] : null;
                IDictionary<string, object?>? parameters = requestLength > 2 ? (IDictionary<string, object?>?)request[2] : null;
                IList<object>? selector = requestLength > 3 ? (IList<object>?)request[3] : null;

                if (id == null || id == "" || !(id is string))
                {
                    return HandleError(400, "Request item should have an ID");
                }
                if (route == null || route == "" || !(route is string))
                {
                    return HandleError(400, "Request item should have a route");
                }
                if (!Regex.IsMatch(route, routeRegex))
                {
                    int routeLength = route.Length;
                    if (routeLength < 2)
                    {
                        return HandleError(400, "Request item route should be at least two characters long");
                    }
                    else if (route[routeLength - 1] == '/')
                    {
                        return HandleError(400, "Request item route should not end in a forward slash");
                    }
                    else if (!Regex.IsMatch(route[0].ToString(), @"[a-zA-Z]"))
                    {
                        return HandleError(400, "Request item route should start with a letter");
                    }
                    else
                    {
                        return HandleError(400, "Request item route should contain only letters, numbers, dashes, underscores, and forward slashes");
                    }
                }
                if (parameters != null && !(parameters is IDictionary<string, object?>))
                {
                    return HandleError(400, "Request item parameters should be a JSON object");
                }
                if (selector != null && !(selector is IList<object>))
                {
                    return HandleError(400, "Request item selector should be a JSON array");
                }
                if (uniqueIds.Contains(id))
                {
                    return HandleError(400, "Request items should have unique IDs");
                }

                uniqueIds.Add(id);

                IList<Delegate> routeHandler;

                if (routes.ContainsKey(route))
                {
                    routeHandler = (IList<Delegate>)routes[route];
                }
                else
                {
                    routeHandler = new List<Delegate> { RouteNotFound };
                }

                IDictionary<string, object?> requestObject = new Dictionary<string, object?>
                {
                    { "id", id },
                    { "route", route },
                    { "parameters", parameters },
                    { "selector", selector }
                };

                tasks.Add(RouteReducer(routeHandler, requestObject, context));
            }

            object?[] results = await Task.WhenAll(tasks);

            return HandleResult(results);
        }

        private object?[] HandleResult(object?[] results)
        {
            return new object?[] { results, null };
        }

        private object?[] HandleError(int code, string message)
        {
            return new object?[] { null, new Dictionary<string, object> { { "code", code }, { "message", message } } };
        }

        private static Action<IDictionary<string, object?>, Dictionary<string, object?>> RouteNotFound(IDictionary<string, object?> parameters, Dictionary<string, object?> context)
        {
            throw new Exception("Route not found");
        }

        private async Task<object?[]> RouteReducer(IList<Delegate> handlerList, IDictionary<string, object?> request, Dictionary<string, object?> context)
        {
            try
            {
                Dictionary<string, object?> safeContext = context != null ? CloneDeep(context) : new Dictionary<string, object?>();
                IDictionary<string, object?> parameters = request["parameters"] as IDictionary<string, object?> ?? new Dictionary<string, object?> {};
                Dictionary<string, object?>? result = null;

                for (int i = 0; i < handlerList.Count; i++)
                {
                    // var tempResult = await handlerList[i](parameters, safeContext);
                    Dictionary<string, object?>? tempResult = null;

                    var delegateItem = handlerList[i];
                    if (delegateItem is Action<IDictionary<string, object?>, Dictionary<string, object?>> syncAction)
                    {
                        try
                        {
                            syncAction.DynamicInvoke(parameters, safeContext);
                        }
                        catch (TargetInvocationException ex)
                        {
                            if (ex.InnerException != null)
                            {
                                throw ex.InnerException;
                            }
                            else
                            {
                                throw ex;
                            }
                        }
                        
                    }
                    else if (delegateItem is Func<IDictionary<string, object?>, Dictionary<string, object?>, Dictionary<string, object?>> syncFunc)
                    {
                        tempResult = syncFunc(parameters, safeContext) as Dictionary<string, object?>;
                    }
                    else if (delegateItem is Func<IDictionary<string, object?>, Dictionary<string, object?>, object> syncObjFunc)
                    {
                        tempResult = syncObjFunc(parameters, safeContext) as Dictionary<string, object?>;
                    }
                    else if (delegateItem is Func<IDictionary<string, object?>, Dictionary<string, object?>, Task<Dictionary<string, object?>>> asyncFunc)
                    {
                        tempResult = await asyncFunc(parameters, safeContext) as Dictionary<string, object?>;
                    }
                    else if (delegateItem is Func<IDictionary<string, object?>, Dictionary<string, object?>, Task<object>> asyncObjFunc)
                    {
                        tempResult = await asyncObjFunc(parameters, safeContext) as Dictionary<string, object?>;
                    }
                    else
                    {
                        throw new Exception("Unsupported delegate type");
                    }

                    if (i == handlerList.Count - 1)
                    {
                        result = tempResult;
                    }
                    else
                    {
                        if (tempResult != null)
                        {
                            throw new Exception("Middleware should not return anything but may mutate context");
                        }
                    }
                }

                if (result != null && !(result is Dictionary<string, object?>))
                {
                    throw new Exception("The result, if any, should be a dictionary");
                }

                if (result != null && request["selector"] is List<object> selector)
                {
                    result = FilterObject(result, selector);
                }

                return new object?[] { request["id"], request["route"], result, (object?) null };
            }
            catch (Exception error)
            {
                Console.WriteLine(error);
                return new object?[] { request["id"], request["route"], (object?) null, new Dictionary<string, object> { { "message", error.Message } } };
            }
        }

        private static Dictionary<string, object?> CloneDeep(Dictionary<string, object?> value)
        {
            var clonedValue = new Dictionary<string, object?>();
            foreach (var pair in value)
            {
                if (pair.Value is Dictionary<string, object?> dict)
                {
                    clonedValue[pair.Key] = CloneDeep(dict);
                }
                else if (pair.Value is object?[] array)
                {
                    clonedValue[pair.Key] = array.Select(item => item is Dictionary<string, object?> nestedDict ? CloneDeep(nestedDict) : item).ToArray();
                }
                else
                {
                    clonedValue[pair.Key] = pair.Value!;
                }
            }
            return clonedValue;
        }

        public static Dictionary<string, object?> FilterObject(Dictionary<string, object?> obj, List<object>? arr)
        {
            if (arr is not null)
            {
                var filteredObj = new Dictionary<string, object?>();
                foreach (var key in arr)
                {
                    if (key is string stringKey && obj.ContainsKey(stringKey))
                    {
                        filteredObj[stringKey] = obj[stringKey];
                    }
                    else if (key is object?[] nestedKey && nestedKey.Length == 2)
                    {
                        var nestedObj = obj.ContainsKey(nestedKey[0]?.ToString() ?? string.Empty) ? obj[nestedKey[0]?.ToString() ?? string.Empty] : null;
                        var nestedArr = nestedKey[1] as List<object>;

                        if (nestedObj is List<object> nestedListObj)
                        {
                            var filteredArr = new List<object>();
                            foreach (var nested in nestedListObj)
                            {
                                if (nested is Dictionary<string, object?> nestedDict)
                                {
                                    var filteredNestedObj = FilterObject(nestedDict, nestedArr);
                                    if (filteredNestedObj?.Count > 0)
                                    {
                                        filteredArr.Add(filteredNestedObj);
                                    }
                                }
                            }

                            if (filteredArr.Count > 0)
                            {
                                filteredObj[nestedKey[0]?.ToString() ?? string.Empty] = filteredArr;
                            }
                        }
                        else if (nestedObj is Dictionary<string, object?> nestedDictObj && nestedDictObj != null)
                        {
                            var filteredNestedObj = FilterObject(nestedDictObj, nestedArr);
                            if (filteredNestedObj?.Count > 0)
                            {
                                filteredObj[nestedKey[0]?.ToString() ?? string.Empty] = filteredNestedObj;
                            }
                        }
                    }
                }
                return filteredObj;
            }
            return obj;
        }
    }
}