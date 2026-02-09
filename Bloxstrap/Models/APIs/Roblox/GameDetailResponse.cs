namespace Bloxstrap.Models.APIs.Roblox
{

    /// <summary>
    /// Roblox.Games.Api.Models.Response.GameDetailResponse
    /// Response model for getting the game detail
    /// </summary>
    public class GameDetailResponse
    {
        /// <summary>
        /// The game universe id
        /// </summary>
        [JsonPropertyName("id")]
        public long Id { get; set; }

        /// <summary>
        /// The game root place id
        /// </summary>
        [JsonPropertyName("rootPlaceId")]
        public long RootPlaceId { get; set; }

        /// <summary>
        /// The game name
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        /// <summary>
        /// The game description
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;

        /// <summary>
        /// The game created time
        /// </summary>
        [JsonPropertyName("created")]
        public DateTime Created { get; set; }

        /// <summary>
        /// The game updated time
        /// </summary>
        [JsonPropertyName("updated")]
        public DateTime Updated { get; set; }

        [JsonPropertyName("creatorType")]
        public string CreatorType { get; set; } = null!;

        [JsonPropertyName("creatorTargetId")]
        public long CreatorTargetId { get; set; }

        [JsonPropertyName("creatorName")]
        public string CreatorName { get; set; } = null!;
    }
}
