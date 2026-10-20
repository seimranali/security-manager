using Microsoft.AspNetCore.Mvc;
using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Application.Services;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Security.API.Controllers;

[ApiController]
[Route("api/security/detections")]
public sealed class DetectionsController(IDetectionService detections) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<DetectionDto>> Get([FromQuery] DetectionQuery query, CancellationToken ct) =>
        detections.GetPagedAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<DetectionDetailDto> Get(Guid id, CancellationToken ct) => detections.GetByIdAsync(id, ct);

    [HttpPut("{id:guid}/status")]
    public Task<DetectionDto> UpdateStatus(Guid id, UpdateDetectionStatusRequest request, CancellationToken ct) =>
        detections.UpdateStatusAsync(id, request, ct);
}
