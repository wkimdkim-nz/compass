// Sends client-side routes to the SPA's entry document.
//
// This runs on the DEFAULT cache behaviour only, so it never sees /api/*. That placement is the
// point: the obvious alternative — CloudFront custom error responses mapping 403/404 to
// /index.html — is configured per *distribution*, not per behaviour, so it would also rewrite the
// API's own 404s into an HTML page with status 200.
function handler(event) {
  var request = event.request;
  var uri = request.uri;

  if (uri.charAt(uri.length - 1) === "/") {
    request.uri = uri + "index.html";
  } else if (uri.lastIndexOf(".") <= uri.lastIndexOf("/")) {
    // Nothing that looks like a file extension in the last segment, so this is a route the
    // browser router owns (/login, /register), not an asset.
    request.uri = "/index.html";
  }

  return request;
}
