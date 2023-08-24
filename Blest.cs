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

    public class Router
    {
        private bool introspection = false;
        private List<Delegate> middleware = new List<Delegate>();
        private List<Delegate> afterware = new List<Delegate>();
        private int timeout = 0;
        public Dictionary<string, RouteConfig> routes = new Dictionary<string, RouteConfig>();

        public Router(Dictionary<string, object?>? options)
        {
            if (options is not null) {
                if (options.ContainsKey("introspection") && options["introspection"] is not null)
                {
                    if (!(options["introspection"] is bool))
                    {
                        throw new Exception("Introspection should be a boolean");
                    }
                    this.introspection = (bool)options["introspection"]!;
                }
                if (options.ContainsKey("timeout") && options["timeout"] is not null)
                {
                    if (!(options["timeout"] is int) || !((int)options["timeout"]! % 1 == 0) || (int)options["timeout"]! <= 0)
                    {
                        throw new Exception("Timeout should be a positive integer");
                    }
                    this.timeout = (int)options["timeout"]!;
                }
            }
        }

        public void Use(params Delegate[] handlers)
        {
            foreach (var handler in handlers)
            {
                if (handler == null)
                {
                    throw new Exception("All arguments should be functions");
                }
                var argCount = handler.Method.GetParameters().Length;
                if (argCount <= 2)
                {
                    middleware.Add(handler);
                }
                else if (argCount == 3)
                {
                    afterware.Add(handler);
                }
                else
                {
                    throw new Exception("Middleware should have at most three arguments");
                }
            }
        }

        public void Map(string route, params Delegate[] handlers)
        {
            var routeError = Utilities.ValidateRoute(route);
            if (routeError != null)
            {
                throw new Exception(routeError);
            }
            else if (routes.ContainsKey(route))
            {
                throw new Exception("Route already exists");
            }
            else if (handlers.Length == 0)
            {
                throw new Exception("At least one handler is required");
            }
            else
            {
                foreach (var handler in handlers)
                {
                    if (handler == null)
                    {
                        throw new Exception("Handlers must be functions");
                    }
                    else if (handler.Method.GetParameters().Length > 2)
                    {
                        throw new Exception("Handlers should have at most two arguments");
                    }
                }
            }

            routes[route] = new RouteConfig
            {
                handler = middleware.Concat(handlers).Concat(this.afterware).ToList(),
                description = null,
                parameters = null,
                result = null,
                visible = this.introspection,
                validate = false,
                timeout = this.timeout
            };
        }

        public void Describe(string route, Dictionary<string, object> config)
        {
            if (!routes.ContainsKey(route))
            {
                throw new Exception("Route does not exist");
            }
            else if (config == null)
            {
                throw new Exception("Configuration should be an object");
            }

            if (config.ContainsKey("description"))
            {
                var description = config["description"];
                if (!(description is string))
                {
                    throw new Exception("Description should be a string");
                }
                routes[route].description = (string)description;
            }

            if (config.ContainsKey("parameters"))
            {
                var parameters = config["parameters"];
                if (!(parameters is Dictionary<string, object>))
                {
                    throw new Exception("Parameters should be a JSON schema");
                }
                routes[route].parameters = (Dictionary<string, object?>)parameters;
            }

            if (config.ContainsKey("result"))
            {
                var result = config["result"];
                if (!(result is Dictionary<string, object>))
                {
                    throw new Exception("Result should be a JSON schema");
                }
                routes[route].result = (Dictionary<string, object?>)result;
            }

            if (config.ContainsKey("visible"))
            {
                var visible = config["visible"];
                if (!(visible is bool))
                {
                    throw new Exception("Visible should be true or false");
                }
                routes[route].visible = (bool)visible;
            }

            if (config.ContainsKey("validate"))
            {
                var validate = config["validate"];
                if (!(validate is bool))
                {
                    throw new Exception("Validate should be true or false");
                }
                routes[route].validate = (bool)validate;
            }

            if (config.ContainsKey("timeout"))
            {
                var timeout = config["timeout"];
                if (!(timeout is int) || !((int)timeout % 1 == 0) || (int)timeout <= 0)
                {
                    throw new Exception("Timeout should be a positive integer");
                }
                routes[route].timeout = (int)timeout;
            }
        }

        public void Merge(Router router)
        {
            if (router == null)
            {
                throw new Exception("Router is required");
            }

            var newRoutes = router.routes.Keys;
            var existingRoutes = routes.Keys;

            if (!newRoutes.Any())
            {
                throw new Exception("No routes to merge");
            }

            foreach (var route in newRoutes)
            {
                if (existingRoutes.Contains(route))
                {
                    throw new Exception("Cannot merge duplicate routes: " + route);
                }
                else
                {
                    routes[route] = new RouteConfig
                    {
                        handler = middleware.Concat(router.routes[route].handler).Concat(afterware).ToList(),
                        timeout = router.routes[route].timeout ?? timeout
                    };
                }
            }
        }

        public void Namespace(string prefix, Router router)
        {
            if (router == null)
            {
                throw new Exception("Router is required");
            }

            var prefixError = Utilities.ValidateRoute(prefix);
            if (prefixError != null)
            {
                throw new Exception(prefixError);
            }

            var newRoutes = router.routes.Keys;
            var existingRoutes = routes.Keys;

            if (!newRoutes.Any())
            {
                throw new Exception("No routes to namespace");
            }

            foreach (var route in newRoutes)
            {
                var nsRoute = $"{prefix}/{route}";
                if (existingRoutes.Contains(nsRoute))
                {
                    throw new Exception("Cannot merge duplicate routes: " + nsRoute);
                }
                else
                {
                    routes[nsRoute] = new RouteConfig
                    {
                        handler = middleware.Concat(router.routes[route].handler!).Concat(afterware).ToList(),
                        timeout = router.routes[route].timeout ?? timeout
                    };
                }
            }
        }

        public object Handle(object[] requests, Dictionary<string, object?>? context = null)
        {
            return RequestHandler.HandleRequest(routes, requests, context);
        }
    }

    public class RouteConfig
    {
        public List<Delegate> handler = new List<Delegate>();
        public string? description;
        public Dictionary<string, object?>? parameters;
        public Dictionary<string, object?>? result;
        public bool? visible;
        public bool? validate;
        public int? timeout;
    }

    public static class Utilities
    {
        private static readonly Regex RouteRegex = new Regex(@"^[a-zA-Z][a-zA-Z0-9_\-\/]*[a-zA-Z0-9]$");

        public static string? ValidateRoute(string route)
        {
            if (string.IsNullOrEmpty(route))
            {
                return "Route is required";
            }
            else if (!RouteRegex.IsMatch(route))
            {
                int routeLength = route.Length;
                if (routeLength < 2)
                {
                    return "Route should be at least two characters long";
                }
                else if (route[routeLength - 1] == '/')
                {
                    return "Route should not end in a forward slash";
                }
                else if (!char.IsLetter(route[0]))
                {
                    return "Route should start with a letter";
                }
                else if (!char.IsLetterOrDigit(route[routeLength - 1]))
                {
                    return "Route should end with a letter or a number";
                }
                else
                {
                    return "Route should contain only letters, numbers, dashes, underscores, and forward slashes";
                }
            }
            else if (RouteRegex.IsMatch(route) && Regex.IsMatch(route, @"\/[^a-zA-Z]"))
            {
                return "Sub-routes should start with a letter";
            }
            else if (RouteRegex.IsMatch(route) && Regex.IsMatch(route, @"[^a-zA-Z0-9]\//"))
            {
                return "Sub-routes should end with a letter or a number";
            }
            else if (RouteRegex.IsMatch(route) && Regex.IsMatch(route, @"\/[a-zA-Z0-9_\-]{0,1}\//"))
            {
                return "Sub-routes should be at least two characters long";
            }
            else if (RouteRegex.IsMatch(route) && Regex.IsMatch(route, @"\/[a-zA-Z0-9_\-]$"))
            {
                return "Sub-routes should be at least two characters long";
            }
            else if (RouteRegex.IsMatch(route) && Regex.IsMatch(route, @"^[a-zA-Z0-9_\-]\//"))
            {
                return "Sub-routes should be at least two characters long";
            }

            return null;
        }
    }

    public class App : Router {
        private Dictionary<string, object?>? options;

        public App(Dictionary<string, object?> options) : base(options)
        {
            this.options = options;
        }

        public Task Run()
        {
            RequestHandler requestHandler = new RequestHandler(this.routes, this.options);
            HttpServer httpServer = new HttpServer(requestHandler, this.options);
            return httpServer.Start();
        }

    }

    public class HttpServer
    {
        private readonly RequestHandler requestHandler;
        private readonly string[] allowedMethods = { "POST", "OPTIONS" };
        private readonly string protocol = "http";
        private readonly string host = "localhost";
        private readonly int port = 8080;
        private readonly Dictionary<string, string> httpHeaders;

        public HttpServer(RequestHandler requestHandler, Dictionary<string, object?>? options = null)
        {
            this.requestHandler = requestHandler;
            this.httpHeaders = new Dictionary<string, string>{
                { "access-control-allow-methods", string.Join(", ", this.allowedMethods) },
                { "access-control-allow-origin", (options is null) ? "" : (options.ContainsKey("accessControlAllowOrigin") && options["accessControlAllowOrigin"] is string) ? (string)options["accessControlAllowOrigin"]! : (options.ContainsKey("cors")) ? (options["cors"] is string) ? (string)options["cors"]! : (options["cors"] is true) ? "*" : "" : "" },
                { "content-security-policy", (options is not null && options.ContainsKey("contentSecurityPolicy") && options["contentSecurityPolicy"] is string) ? (string)options["contentSecurityPolicy"]! : "default-src 'self';base-uri } 'self';font-src 'self' https: data:;form-action 'self';frame-ancestors 'self';img-src 'self' data:;object-src 'none';script-src 'self' };script-src-attr 'none';style-src 'self' https: 'unsafe-inline';upgrade-insecure-requests" },
                { "cross-origin-opener-policy", (options is not null && options.ContainsKey("crossOriginOpenerPolicy") && options["crossOriginOpenerPolicy"] is string) ? (string)options["crossOriginOpenerPolicy"]! : "same-origin" },
                { "cross-origin-resource-policy", (options is not null && options.ContainsKey("crossOriginResourcePolicy") && options["crossOriginResourcePolicy"] is string) ? (string)options["crossOriginResourcePolicy"]! : "same-origin" },
                { "origin-agent-cluster", (options is not null && options.ContainsKey("originAgentCluster") && options["originAgentCluster"] is string) ? (string)options["originAgentCluster"]! : "?1" },
                { "referrer-policy", (options is not null && options.ContainsKey("referrerPolicy") && options["referrerPolicy"] is string) ? (string)options["referrerPolicy"]! : "no-referrer" },
                { "strict-transport-security", (options is not null && options.ContainsKey("strictTransportSecurity") && options["strictTransportSecurity"] is string) ? (string)options["strictTransportSecurity"]! : "max-age=15552000; includeSubDomains" },
                { "x-content-type-options", (options is not null && options.ContainsKey("xContentTypeOptions") && options["xContentTypeOptions"] is string) ? (string)options["xContentTypeOptions"]! : "nosniff" },
                { "x-dns-prefetch-control", (options is not null && options.ContainsKey("xDnsPrefetchOptions") && options["xDnsPrefetchOptions"] is string) ? (string)options["xDnsPrefetchOptions"]! : "off" },
                { "x-download-options", (options is not null && options.ContainsKey("xDownloadOptions") && options["xDownloadOptions"] is string) ? (string)options["xDownloadOptions"]! : "noopen" },
                { "x-frame-options", (options is not null && options.ContainsKey("xFrameOptions") && options["xFrameOptions"] is string) ? (string)options["xFrameOptions"]! : "SAMEORIGIN" },
                { "x-permitted-cross-domain-policies", (options is not null && options.ContainsKey("xPermittedCrossDomainPolicies") && options["xPermittedCrossDomainPolicies"] is string) ? (string)options["xPermittedCrossDomainPolicies"]! : "none" },
                { "x-xss-protection", (options is not null && options.ContainsKey("xXssProtection") && options["xXssProtection"] is string) ? (string)options["xXssProtection"]! : "0" }
            };
            if (options is not null)
            {
                if (options.ContainsKey("protocol") && options["protocol"] is not null)
                {
                    if (options["protocol"] is string)
                    {
                        throw new Exception("\"protocol\" option should be a string");
                    }
                    this.protocol = (string)options["protocol"]!;
                }
                if (options.ContainsKey("host") && options["host"] is not null)
                {
                    if (options["host"] is string)
                    {
                        throw new Exception("\"host\" option should be a string");
                    }
                    this.host = (string)options["host"]!;
                }
                if (options.ContainsKey("port") && options["port"] is not null)
                {
                    if (options["port"] is int)
                    {
                        throw new Exception("\"port\" option should be an int");
                    }
                    this.port = (int)options["port"]!;
                }
            }
        }

        public async Task Start()
        {
            var listener = new HttpListener();
            string prefix = this.protocol + "://" + this.host + ":" + this.port + "/";
            listener.Prefixes.Add(prefix);
            listener.Start();

            while (true)
            {
                HttpListenerContext context = await listener.GetContextAsync();
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                foreach (string key in this.httpHeaders.Keys)
                {
                    string value = this.httpHeaders[key];
                    response.Headers.Add(key, value);
                }

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
        private readonly int maxBatchSize = 25;
        private readonly int bufferDelay = 10;
        private readonly Dictionary<string, object?> headers = new Dictionary<string, object?>();
        private readonly List<object?[]> queue = new List<object?[]>();
        private Timer? timeout;
        private readonly System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();
        private readonly Dictionary<string, TaskCompletionSource<object>> pendingRequests = new Dictionary<string, TaskCompletionSource<object>>();

        public HttpClient(string url)
        {
            this.url = url;
        }
        
        public HttpClient(string url, Dictionary<string, object?> options)
        {
            this.url = url;
            if (options is not null)
            {
                string? optionError = ValidateClientOptions(options);
                if (optionError is not null)
                {
                    throw new Exception(optionError);
                }
                if (options["maxBatchSize"] is int maxBatchSize)
                {
                    this.maxBatchSize = maxBatchSize;
                }
                if (options["bufferDelay"] is int bufferDelay)
                {
                    this.bufferDelay = bufferDelay;
                }
                if (options["headers"] is Dictionary<string, object?> headers)
                {
                    this.headers = headers;
                    if (headers.TryGetValue("Authorization", out var authorizationObj) && authorizationObj is string authorization)
                    {
                        string[] words = authorization.Split(' ', 2);
                        this.client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(words[0], words.Length > 1 ? words[1] : null);
                    }
                }
            }
        }

        private static string? ValidateClientOptions(Dictionary<string, object?> options)
        {
            if (options == null)
            {
                return null;
            }
            else
            {
                if (options["headers"] is not null)
                {
                    if (!(options["headers"] is IDictionary<string, object>))
                    {
                        return "\"headers\" option should be a dictionary";
                    }
                }
                if (options["maxBatchSize"] is not null)
                {
                    if (!(options["maxBatchSize"] is int))
                    {
                        return "\"maxBatchSize\" option should be an integer";
                    }
                    else if ((int)options["maxBatchSize"]! < 1)
                    {
                        return "\"maxBatchSize\" option should be greater than or equal to one";
                    }
                }
                if (options["bufferDelay"] is not null)
                {
                    if (!(options["bufferDelay"] is int))
                    {
                        return "\"bufferDelay\" option should be an integer";
                    }
                    else if ((int)options["bufferDelay"]! < 0)
                    {
                        return "\"bufferDelay\" option should be greater than or equal to zero";
                    }
                }
            }
            return null;
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
            if (queue.Count == 0)
            {
                return;
            }
            timeout?.Dispose();
            timeout = null;
            List<object?[]> copyQueue;
            lock (queue)
            {
                copyQueue = queue.ToList();
                queue.RemoveRange(0, queue.Count);
            }
            int batchCount = (int)Math.Round((double)(copyQueue.Count / this.maxBatchSize));
            for (int i = 0; i <= batchCount; i++)
            {
                List<object?[]> newQueue = copyQueue.GetRange(i * this.maxBatchSize, (i + 1) * this.maxBatchSize);
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
        private readonly Dictionary<string, RouteConfig> routes;
        private readonly Dictionary<string, object?>? options;

        public RequestHandler(Dictionary<string, object> routes, Dictionary<string, object?>? options = null)
        {
            if (routes == null || routes.Count == 0)
            {
                throw new Exception("A routes object is required");
            }

            var routeKeys = new List<string>(routes.Keys);
            var myRoutes = new Dictionary<string, RouteConfig>();

            foreach (var key in routeKeys)
            {
                var routeError = Utilities.ValidateRoute(key);
                if (routeError != null)
                {
                    throw new Exception(routeError + ": " + key);
                }

                var route = routes[key];
                if (route is List<Delegate> routeList)
                {
                    if (routeList.Count == 0)
                    {
                        throw new Exception("Route has no handlers: " + key);
                    }

                    foreach (var handler in routeList)
                    {
                        if (handler == null)
                        {
                            throw new Exception("All route handlers must be functions: " + key);
                        }
                    }

                    myRoutes[key] = new RouteConfig
                    {
                        handler = routeList
                    };
                }
                else if (route is Dictionary<string, object> routeDictionary)
                {
                    if (!routeDictionary.ContainsKey("handler"))
                    {
                        throw new Exception("Route has no handlers: " + key);
                    }

                    if (routeDictionary["handler"] is Delegate handlerFunction)
                    {
                        myRoutes[key] = new RouteConfig
                        {
                            handler = new List<Delegate> { handlerFunction }
                        };
                    }
                    else if (routeDictionary["handler"] is List<Delegate> handlerList)
                    {
                        foreach (var handler in handlerList)
                        {
                            if (handler == null)
                            {
                                throw new Exception("All route handlers must be functions: " + key);
                            }
                        }

                        myRoutes[key] = new RouteConfig
                        {
                            handler = handlerList
                        };
                    }
                    else
                    {
                        throw new Exception("Route handler is not valid: " + key);
                    }
                }
                else if (route is Delegate singleHandlerFunction)
                {
                    myRoutes[key] = new RouteConfig
                    {
                        handler = new List<Delegate> { singleHandlerFunction }
                    };
                }
                else
                {
                    throw new Exception("Route is missing handler: " + key);
                }
            }

            this.routes = myRoutes;
            this.options = options;
        }

        public RequestHandler(Dictionary<string, RouteConfig> routes, Dictionary<string, object?>? options = null)
        {
            this.routes = routes;
            this.options = options;
        }

        public async Task<object?[]> Handle(IList<object> requests, IDictionary<string, object?>? context)
        {
            Dictionary<string, RouteConfig> routes = this.routes;
            return await HandleRequest(routes, requests, context);
        }

        public static async Task<object?[]> HandleRequest(IDictionary<string, RouteConfig> routes, IList<object> requests, IDictionary<string, object?>? context)
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

            foreach (var request in requests)
            {
                if (!(request is List<object> requestList))
                {
                    return HandleError(400, "Request item should be an array");
                }

                string? id = requestList.Count > 0 ? requestList[0] as string : null;
                string? route = requestList.Count > 1 ? requestList[1] as string : null;
                Dictionary<string, object>? parameters = requestList.Count > 2 ? requestList[2] as Dictionary<string, object> : null;
                List<object>? selector = requestList.Count > 3 ? requestList[3] as List<object> : null;

                if (string.IsNullOrEmpty(id))
                {
                    return HandleError(400, "Request item should have an ID");
                }
                if (string.IsNullOrEmpty(route))
                {
                    return HandleError(400, "Request item should have a route");
                }
                if (parameters != null && !(parameters is Dictionary<string, object>))
                {
                    return HandleError(400, "Request item parameters should be a JSON object");
                }
                if (selector != null && !(selector is List<object>))
                {
                    return HandleError(400, "Request item selector should be a JSON array");
                }

                if (uniqueIds.Contains(id))
                {
                    return HandleError(400, "Request items should have unique IDs");
                }
                uniqueIds.Add(id);

                RouteConfig? thisRoute = routes.ContainsKey(route) ? routes[route] : null;
                List<Delegate> routeHandler = thisRoute?.handler ?? new List<Delegate> { RouteNotFound };

                Dictionary<string, object?> requestObject = new Dictionary<string, object?>
                {
                    { "id", id },
                    { "route", route },
                    { "parameters", parameters },
                    { "selector", selector },
                };

                Dictionary<string, object?> myContext = new Dictionary<string, object?>
                {
                    { "requestId", id },
                    { "routeName", route },
                    { "selector", selector },
                    { "requestTime", DateTime.Now },
                };

                foreach (var item in context)
                {
                    myContext[item.Key] = item.Value;
                }

                if (thisRoute?.timeout is not null) {
                    tasks.Add(RouteReducerWithTimeout(routeHandler, requestObject, myContext, thisRoute?.timeout));
                } else {
                    tasks.Add(RouteReducer(routeHandler, requestObject, myContext));
                }
            }

            object?[] results = await Task.WhenAll(tasks);

            return HandleResult(results);
        }

        private static object?[] HandleResult(object?[] results)
        {
            return new object?[] { results, null };
        }

        private static object?[] HandleError(int status, string message)
        {
            return new object?[] { null, new Dictionary<string, object> { { "status", status }, { "message", message } } };
        }

        private static Action<IDictionary<string, object?>, Dictionary<string, object?>> RouteNotFound(IDictionary<string, object?> parameters, Dictionary<string, object?> context)
        {
            throw new Exception("Route not found");
        }

        static void TimerCallback(object? state)
        {
            if (state is not null)
            {
                var cancellationTokenSource = (CancellationTokenSource)state;
                cancellationTokenSource.Cancel();
            }
        }

        private static async Task<object?[]> RouteReducerWithTimeout(IList<Delegate> handlerList, IDictionary<string, object?> request, Dictionary<string, object?> context, int? timeout)
        {
            int timeoutDuration = timeout is not null && (int)timeout > 0 ? (int)timeout : 3600000;
            using (var cancellationTokenSource = new CancellationTokenSource(timeoutDuration))
            {
                var timer = new Timer(TimerCallback, cancellationTokenSource.Token, Timeout.Infinite, Timeout.Infinite);
                object?[] result = await RouteReducer(handlerList, request, context);
                timer.Dispose();
                return result;
            }
        }

        private static async Task<object?[]> RouteReducer(IList<Delegate> handlerList, IDictionary<string, object?> request, Dictionary<string, object?> context)
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
                    else if (delegateItem is Func<IDictionary<string, object?>, Dictionary<string, object?>, object?> syncAnyFunc)
                    {
                        tempResult = syncAnyFunc(parameters, safeContext) as Dictionary<string, object?>;
                    }
                    else if (delegateItem is Func<IDictionary<string, object?>, Dictionary<string, object?>, Task<Dictionary<string, object?>>> asyncFunc)
                    {
                        tempResult = await asyncFunc(parameters, safeContext) as Dictionary<string, object?>;
                    }
                    else if (delegateItem is Func<IDictionary<string, object?>, Dictionary<string, object?>, Task<object>> asyncObjFunc)
                    {
                        tempResult = await asyncObjFunc(parameters, safeContext) as Dictionary<string, object?>;
                    }
                    else if (delegateItem is Func<IDictionary<string, object?>, Dictionary<string, object?>, Task<object?>> asyncAnyFunc)
                    {
                        tempResult = await asyncAnyFunc(parameters, safeContext) as Dictionary<string, object?>;
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