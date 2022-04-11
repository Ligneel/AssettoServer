namespace AssettoServer.Server.TrackParams
{
    public class TrackParams
    {
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public string? Timezone { get; set; }//lig edit: replaced init by set
        public string? Name { get; init; }
    }
}