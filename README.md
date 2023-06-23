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

## Installation

Install BLEST.NET from NuGet

```bash

```

## Usage

Use the `RequestHandler` class to create a request handler suitable for use in an existing .NET application. Use the `HttpServer` class to create a standalone HTTP server for your request handler. Use the `HttpClient` class to create a BLEST HTTP client.

### RequestHandler

```c#
const express = require('express')
const { createRequestHandler } = require('blest-js')

const app = express()
const port = 8080

// Create some middleware (optional)
const authMiddleware = (params, context) => {
  if (params?.name) {
    context.user = {
      name: params.name
    }
  } else {
    throw new Error('Unauthorized')
  }
}

// Create a route controller
const greetController = (params, context) => {
  return {
    greeting: `Hi, ${context.user?.name}!`
  }
}

// Create a request handler
const requestHandler = createRequestHandler({
    greet: [authMiddleware, greetController]
})

// Parse the JSON body
app.use(express.json())

// Use the request handler
app.post('/', async (req, res, next) => {
  const [result, error] = await requestHandler(req.body, {
    headers: req.headers
  })
  if (error) {
    return next(error)
  } else {
    res.json(result)
  }
});

// Listen for requests
app.listen(port, () => {
  console.log(`Server listening on port ${port}`)
})
```

### HttpServer

```c#
const { createHttpServer } = require('blest-js')

const port = 8080

// Create some middleware (optional)
const authMiddleware = (params, context) => {
  if (params?.name) {
    context.user = {
      name: params.name
    }
  } else {
    throw new Error('Unauthorized')
  }
}

// Create a route controller
const greetController = (params, context) => {
  return {
    greeting: `Hi, ${context.user?.name}!`
  }
}

// Create a request handler
const requestHandler = createRequestHandler({
    greet: [authMiddleware, greetController]
})

// Create a server
const server = createHttpServer(requestHandler)

// Listen for requests
server.listen(port, () => {
  console.log(`Server listening on port ${port}`)
})
```

### HttpClient

```c#
const { createHttpClient } = require('blest-js')

// Create a client
const request = createHttpClient('http://localhost:8080', {
  headers: {
    'Authorization': 'Bearer token'
  }
})

// Send a request
request('greet', { name: 'Steve' }, ['greeting'])
.then((result) => {
  // Do something with the result
})
.catch((error) => {
  // Do something in case of error
})
```

## License

This project is licensed under the [MIT License](LICENSE).