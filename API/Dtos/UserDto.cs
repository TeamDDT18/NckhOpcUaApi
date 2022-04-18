using System.ComponentModel.DataAnnotations;

namespace API.Dtos
{
    public class UserDto
    {
        [Required]
        public string? Username { get; set; }
        public string? Token { get; set; }
    }
}
