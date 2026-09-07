// The read surface arrives at 1.5, and the pages it hosts at 1.6. This host
// exists so the project runs and so api-isolation has a compiled dependency
// file to read. It maps nothing on purpose: an endpoint written here would be
// 1.5's work done early, and 1.5's done condition is that the API computes and
// fetches nothing.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
