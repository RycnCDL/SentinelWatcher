using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[Route("api/[controller]")]
public class DcrController : WorkspaceControllerBase
{
    private readonly WorkspaceCacheManager _cacheManager;
    private readonly bool _enabled;

    public DcrController(
        WorkspaceRegistry registry,
        WorkspaceCacheManager cacheManager,
        IOptions<SentinelOptions> options)
        : base(registry)
    {
        _cacheManager = cacheManager;
        _enabled = options.Value.EnableDcrPage;
    }

    [HttpGet]
    public async Task<ActionResult<DcrResponse>> Get([FromQuery] string? workspace)
    {
        // Disabled by configuration (Sentinel:EnableDcrPage): no DCR is read.
        if (!_enabled)
            return NotFound(new { message = "The Data Collection Rules page is disabled." });

        var ws = ResolveWorkspace(workspace, out var error);
        if (ws is null) return error!;

        await _cacheManager.EnsureDataAvailableAsync(ws, HttpContext.RequestAborted);

        var entry = _cacheManager.GetOrCreateEntry(ws);
        return Section(entry.Dcr, entry.DcrError);
    }
}
