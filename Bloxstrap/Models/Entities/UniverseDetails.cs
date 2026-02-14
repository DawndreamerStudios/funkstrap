namespace Bloxstrap.Models.Entities
{
    /// <summary>
    /// Explicit loading. Load from cache before and after a fetch.
    /// </summary>
    public class UniverseDetails
    {
        private static List<UniverseDetails> _cache { get; set; } = new();

        public GameDetailResponse Data { get; set; } = null!;

        public GameCreator Creator { get; set; } = null!;

        public long ID { get; set; } = -1;

        /// <summary>
        /// Returns data for a 128x128 icon
        /// </summary>
        public ThumbnailResponse Thumbnail { get; set; } = null!;

        public static UniverseDetails? LoadFromCache(long id)
        {
            var cacheQuery = _cache.Where(x => x.ID == id);

            if (cacheQuery.Any())
                return cacheQuery.First();

            return null;
        }

        public static Task FetchSingle(long id) => FetchBulk(id.ToString());

        public static async Task FetchBulk(string ids)
        {
            var universeThumbnailResponse = await Http.GetJson<ApiArrayResponse<ThumbnailResponse>>($"https://thumbnails.roblox.com/v1/games/icons?universeIds={ids}&returnPolicy=PlaceHolder&size=128x128&format=Png&isCircular=false");

            if (!universeThumbnailResponse.Data.Any())
                throw new InvalidHTTPResponseException("Roblox API for Game Thumbnails returned invalid data");

            foreach (string strId in ids.Split(','))
            {
                long id = long.Parse(strId);

                var gameDetailResponse = await Http.GetJson<GameDetailResponse>($"https://develop.roblox.com/v1/universes/{strId}");

                if (gameDetailResponse == null)
                    throw new InvalidHTTPResponseException("Roblox API for Game Details returned invalid data");

                string creatorApiRoute = gameDetailResponse.CreatorType == "Group" ? "https://groups.roblox.com/v1/groups/" : "https://users.roblox.com/v1/users/";
                
                var gameCreator = await Http.GetJson<GameCreator>($"{creatorApiRoute}{gameDetailResponse.CreatorTargetId.ToString()}");
                
                _cache.Add(new UniverseDetails
                {
                    ID = id,
                    Data = gameDetailResponse,
                    Creator = gameCreator,
                    Thumbnail = universeThumbnailResponse.Data.Where(x => x.TargetId == id).First(),
                });
            }
        }
    }
}
