# BLEST.NET

The .NET reference implementation of BLEST (Batch-able, Lightweight, Encrypted State Transfer), an improved communication protocol for web APIs which leverages JSON, supports request batching and selective returns, and provides a modern alternative to REST.

To learn more about BLEST, please refer to the white paper: https://jhunt.dev/BLEST%20White%20Paper.pdf

For a front-end implementation in React, please visit https://github.com/jhunt/blest-react

## Features

- Built on JSON - Reduce parsing time and overhead
- Request Batching - Save bandwidth and reduce load times
- Compact Payloads - Save more bandwidth
- Selective Returns - Save even more bandwidth
- Single Endpoint - Reduce complexity and improve data privacy
- Fully Encrypted - Improve data privacy

<!-- ## Installation

Install BLEST.NET from NuGet

```bash

``` -->

## Usage

Use the `RequestHandler` class to create a request handler suitable for use in an existing .NET application. Use the `HttpServer` class to create a standalone HTTP server for your request handler. Use the `HttpClient` class to create a BLEST HTTP client.

### RequestHandler

```c#
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Blest;

// Create some middleware (optional)
var authMiddleware = new Action<IDictionary<string, object?>, Dictionary<string, object?>>((parameters, context) =>
{
  if (parameters.ContainsKey("name"))
  {
    context["user"] = new Dictionary<string, object?>
    {
      { "name", parameters["name"] }
    };
  }
  else
  {
    throw new Exception("Unauthorized");
  }
});

// Create a route controller
var greetController = new Func<IDictionary<string, object?>, Dictionary<string, object?>, Dictionary<string, object?>>((parameters, context) =>
{
  if (!context.ContainsKey("user") || context["user"] == null || !((Dictionary<string, object?>)context["user"]).ContainsKey("name"))
  {
    throw new Exception("Unauthorized");
  }

  return new Dictionary<string, object?>
  {
    { "greeting", "Hi, " + ((Dictionary<string, object?>)context["user"])["name"] + "!" }
  };
});

// Define your router
var router = new Dictionary<string, List<Delegate>>
{
  { "greet", new List<Delegate> { authMiddleware, greetController } }
};

// Create a request handler
var requestHandler = new RequestHandler(router);

// Parse incoming JSON payload
Dictionary<string, object?> payload = YourCustomJsonSerializer(request.Body);

// Assemble context object
Dictionary<string, object?> context = {
  { "headers", request.Headers }
};

// Use the request handler
object?[] resultError = await requestHandler.Handle(payload, context);
dynamic? result = resultError[0];
dynamic? error = resultError[1];
if (error != null)
{
  // Do something in case of error
}
else
{
  // Do something with the result
}
```

### HttpServer

```c#
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Blest;

class Program
{
  public static async Task Main()
  {
    // Create some middleware (optional)
    var authMiddleware = new Action<IDictionary<string, object?>, Dictionary<string, object?>>((parameters, context) =>
    {
        if (parameters.ContainsKey("name"))
        {
            context["user"] = new Dictionary<string, object?>
            {
                { "name", parameters["name"] }
            };
        }
        else
        {
            throw new Exception("Unauthorized");
        }
    });

    // Create a route controller
    var greetController = new Func<IDictionary<string, object?>, Dictionary<string, object?>, Dictionary<string, object?>>((parameters, context) =>
    {
        if (!context.ContainsKey("user") || context["user"] == null || !((Dictionary<string, object?>)context["user"]).ContainsKey("name"))
        {
            throw new Exception("Unauthorized");
        }

        return new Dictionary<string, object?>
        {
            { "greeting", "Hi, " + ((Dictionary<string, object?>)context["user"])["name"] + "!" }
        };
    });

    // Define your router
    var router = new Dictionary<string, List<Delegate>>
    {
        { "greet", new List<Delegate> { authMiddleware, greetController } }
    };

    // Create a request handler
    var requestHandler = new RequestHandler(router);

    // Create a server
    HttpServer server = new HttpServer(requestHandler);

    // Listen for requests
    Console.WriteLine("Server listening on port 8080");
    await server.Listen("http://localhost:8080/");
  }
}
```

### HttpClient

```c#
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Blest;

class Program
{
  static async Task Main()
  {
    // Create a client
    Dictionary<string, object?> options = new Dictionary<string, object?>
    {
      ["headers"] = new Dictionary<string, object?>
      {
        ["Authorization"] = "Bearer token"
      }
    };
    var client = new HttpClient("http://localhost:8080", options);

    try
    {
      // Send a request
      var selector = new object[] { "greeting" };
      IDictionary<string, object?> parameters = new Dictionary<string, object?> {
          { "name", "Steve" }
      };
      var result = await client.Request("greet", parameters, selector);
      // Do something with the result
    }
    catch (Exception error)
    {
      // Do something in case of error
    }
  }
}
```

## License

This project is licensed under the [MIT License](LICENSE).