# BLEST .NET

The .NET reference implementation of BLEST (Batch-able, Lightweight, Encrypted State Transfer), an improved communication protocol for web APIs which leverages JSON, supports request batching by default, and provides a modern alternative to REST.

To learn more about BLEST, please visit the website: https://blest.jhunt.dev

For a front-end implementation in React, please visit https://github.com/jhuntdev/blest-react

## Features

- Built on JSON - Reduce parsing time and overhead
- Request Batching - Save bandwidth and reduce load times
- Compact Payloads - Save even more bandwidth
- Single Endpoint - Reduce complexity and improve data privacy
- Fully Encrypted - Improve data privacy

## Installation

Install BLEST .NET from NuGet

```bash
dotnet add package Blest
```

## Usage

The `Blest` class of this library has an interface similar to ASP.NET Core. It also provides a `Router` class with a `Handle` method for use in an existing .NET API and an `HttpClient` class with a `Request` method for making BLEST HTTP requests.

```c#
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Blest;

// Instantiate your app
var app = new Blest({ "timeout": 1000, "cors": true });

// Create some middleware (optional)
app.Use(async (body, context) =>
{
  if (context.ContainsKey("headers") && context["headers"]["auth"] === "myToken")
  {
    context["user"] = new Dictionary<string, object?>
    {
      // user info for example
    };
  }
  else
  {
    throw new Exception("Unauthorized");
  }
});

// Create a route controller
app.Map('greet', async (body, context) =>
{
  return new Dictionary<string, object?>
  {
    { "greeting", "Hi, " + body["name"] + "!" }
  };
});

// Start your BLEST server
app.Run();
```

### Router

```c#
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Blest;

// Instantiate your Router
var router = new Router({ "timeout": 1000 });

// Create some middleware (optional)
router.Use(async (body, context) =>
{
  if (context.ContainsKey("headers") && context["headers"]["auth"] === "myToken")
  {
    context["user"] = new Dictionary<string, object?>
    {
      // user info for example
    };
  }
  else
  {
    throw new Exception("Unauthorized");
  }
});

// Create a route controller
router.Map('greet', async (body, context) =>
{
  return new Dictionary<string, object?>
  {
    { "greeting", "Hi, " + ((Dictionary<string, object?>)context["user"])["name"] + "!" }
  };
});

// ...later in your application
// Handle a request
result, error = router.Handle(jsonData, context);
if (error is not null)
{
  // do something in case of error
}
else
{
  // do something with the result
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
      ["httpHeaders"] = new Dictionary<string, object?>
      {
        ["Authorization"] = "Bearer token"
      }
    };
    var client = new HttpClient("http://localhost:8080", options);

    try
    {
      // Send a request
      IDictionary<string, object?> body = new Dictionary<string, object?> {
          { "name", "Steve" }
      };
      IDictionary<string, object?> headers = new Dictionary<string, object?> {
          { "auth", "myToken" }
      };
      var result = await client.Request("greet", body, headers);
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