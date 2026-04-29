using FlexCms.Framework.Modules.Attributes;

namespace FlexCms.Sample.Hello.Services;

[FcmsScoped]
public class HelloService
{
    public string Greet(string? name) => $"Hello, {name ?? "world"}!";
}
