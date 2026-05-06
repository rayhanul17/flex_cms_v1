using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FlexCms.Framework.Models;

/// <summary>
/// jQuery DataTables 2.x sends server-side requests as form-encoded with
/// bracket-notation keys: <c>search[value]=…&amp;order[0][column]=0&amp;order[0][dir]=asc</c>.
/// ASP.NET Core's default model binder doesn't translate <c>search[value]</c> into
/// <c>Search.Value</c> — it treats brackets as collection indexers. This binder
/// reads <see cref="HttpRequest.Form"/> directly and maps the well-known keys
/// onto <see cref="DataTablesRequest"/>.
/// </summary>
public sealed class DataTablesRequestModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var req = new DataTablesRequest();
        var form = bindingContext.HttpContext.Request.HasFormContentType
            ? bindingContext.HttpContext.Request.Form
            : null;
        var query = bindingContext.HttpContext.Request.Query;

        string? Get(string key)
        {
            if (form is not null && form.TryGetValue(key, out var fv) && fv.Count > 0) return fv[0];
            if (query.TryGetValue(key, out var qv) && qv.Count > 0) return qv[0];
            return null;
        }

        req.Draw   = int.TryParse(Get("draw"),   out var d) ? d : 0;
        req.Start  = int.TryParse(Get("start"),  out var s) ? s : 0;
        req.Length = int.TryParse(Get("length"), out var l) ? l : 25;

        req.Search = new DataTablesSearch
        {
            Value = Get("search[value]") ?? "",
            Regex = bool.TryParse(Get("search[regex]"), out var sr) && sr
        };

        // Order rows — usually one entry but support N
        for (int i = 0; ; i++)
        {
            var col = Get($"order[{i}][column]");
            if (col is null) break;
            req.Order.Add(new DataTablesOrder
            {
                Column = int.TryParse(col, out var oc) ? oc : 0,
                Dir = Get($"order[{i}][dir]") ?? "asc"
            });
        }

        // Columns metadata — needed for column-specific search
        for (int i = 0; ; i++)
        {
            var data = Get($"columns[{i}][data]");
            if (data is null) break;
            req.Columns.Add(new DataTablesColumn
            {
                Data = data,
                Name = Get($"columns[{i}][name]") ?? "",
                Searchable = !bool.TryParse(Get($"columns[{i}][searchable]"), out var cs) || cs,
                Orderable  = !bool.TryParse(Get($"columns[{i}][orderable]"),  out var co) || co,
                Search = new DataTablesSearch
                {
                    Value = Get($"columns[{i}][search][value]") ?? "",
                    Regex = bool.TryParse(Get($"columns[{i}][search][regex]"), out var csr) && csr
                }
            });
        }

        bindingContext.Result = ModelBindingResult.Success(req);
        return Task.CompletedTask;
    }
}

/// <summary>
/// MVC discovery hook — applies <see cref="DataTablesRequestModelBinder"/> to
/// any action parameter of type <see cref="DataTablesRequest"/> automatically.
/// </summary>
public sealed class DataTablesRequestModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Metadata.ModelType == typeof(DataTablesRequest)
            ? new DataTablesRequestModelBinder()
            : null;
    }
}
