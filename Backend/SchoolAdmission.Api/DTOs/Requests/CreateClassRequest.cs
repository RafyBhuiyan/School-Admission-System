using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SchoolAdmission.Api.DTOs.Requests;

public sealed class CreateClassRequest
{
    private string? name;

    [Required(ErrorMessage = "Class name is required.")]
    [StringLength(200, ErrorMessage = "Class name must be at most 200 characters.")]
    public string? Name
    {
        get => name;
        set => name = value?.Trim();
    }

    [Required(ErrorMessage = "Total seats is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "Total seats must be a positive integer.")]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int? TotalSeats { get; set; }
}
