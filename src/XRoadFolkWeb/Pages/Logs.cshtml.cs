using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using XRoadFolkWeb.Infrastructure.Logging;

namespace XRoadFolkWeb.Pages;

public class LogsModel : PageModel
{
    private readonly InMemoryLogStore _store;

    public LogsModel(InMemoryLogStore store) => _store = store;

    public string LogText { get; private set; } = string.Empty;

    public void OnGet()
    {
        var lines = _store.Snapshot()
            .Select(e => $"[{e.Timestamp:u}] {e.Level} {e.Category} ({e.EventId.Id}) - {e.Message}{(e.Exception is not null ? "\n" + e.Exception : string.Empty)}");
        LogText = string.Join(Environment.NewLine, lines);
    }

    public IActionResult OnGetStream()
    {
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Content-Type"] = "text/event-stream";
        return new LogStreamResult(_store);
    }

    public IActionResult OnPostClear()
    {
        _store.Clear();
        return RedirectToPage();
    }

    private sealed class LogStreamResult : IActionResult
    {
        private readonly InMemoryLogStore _store;
        public LogStreamResult(InMemoryLogStore store) => _store = store;

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var resp = context.HttpContext.Response;
            resp.Headers["Cache-Control"] = "no-cache";
            resp.ContentType = "text/event-stream";
            await using var writer = new StreamWriter(resp.Body);

            long lastId = 0;
            // initial dump
            foreach (var e in _store.Snapshot())
            {
                lastId = e.Id;
                await writer.WriteAsync($"id: {e.Id}\n");
                await writer.WriteAsync($"data: [{e.Timestamp:u}] {e.Level} {e.Category} ({e.EventId.Id}) - {e.Message}\n\n");
                await writer.FlushAsync();
            }

            // stream new entries
            while (!context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                var news = _store.GetSince(lastId);
                foreach (var e in news)
                {
                    lastId = e.Id;
                    await writer.WriteAsync($"id: {e.Id}\n");
                    await writer.WriteAsync($"data: [{e.Timestamp:u}] {e.Level} {e.Category} ({e.EventId.Id}) - {e.Message}\n\n");
                }
                await writer.FlushAsync();
                await Task.Delay(1000, context.HttpContext.RequestAborted).ContinueWith(_ => { });
            }
        }
    }
}
