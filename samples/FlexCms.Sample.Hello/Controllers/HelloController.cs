using FlexCms.Sample.Hello.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Sample.Hello.Controllers;

[Route("hello")]
public class HelloController : Controller
{
    private readonly HelloService _hello;

    public HelloController(HelloService hello) => _hello = hello;

    [HttpGet("")]
    public IActionResult Index([FromQuery] string? name)
        => Content(_hello.Greet(name), "text/plain");
}
