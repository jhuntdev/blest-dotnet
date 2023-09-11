# BLEST .NET

The .NET reference implementation of BLEST (Batch-able, Lightweight, Encrypted State Transfer), an improved communication protocol for web APIs which leverages JSON, supports request batching and selective returns, and provides a modern alternative to REST.

To learn more about BLEST, please visit the website: https://blest.jhunt.dev

For a front-end implementation in React, please visit https://github.com/jhuntdev/blest-react

## Features

- Built on JSON - Reduce parsing time and overhead
- Request Batching - Save bandwidth and reduce load times
- Compact Payloads - Save more bandwidth
- Selective Returns - Save even more bandwidth
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
app.Use(async (parameters, context) =>
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
app.Map('greet', async (parameters, context) =>
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
router.Use(async (parameters, context) =>
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
router.Map('greet', async (parameters, context) =>
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
      ["headers"] = new Dictionary<string, object?>
      {
        ["Authorization"] = "Bearer token"
      }
    };
    var client = new HttpClient("http://localhost:8080", options);

    try
    {
      // Send a request
      IDictionary<string, object?> parameters = new Dictionary<string, object?> {
          { "name", "Steve" }
      };
      var selector = new object[] { "greeting" };
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