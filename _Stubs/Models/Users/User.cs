namespace IDV_Backend.Models.Users
{
    // Test-only minimal stub so we can seed Users for Invitations queries.
    // Matches the properties used by the service/repository projections.
    public sealed class User
    {
        public long Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
    }
}
