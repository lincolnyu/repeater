namespace MediaPlayerCom
{
    public static class TrackHelper
    {
        public static string PathToTrackName(this string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
            var index = path.LastIndexOfAny(new []
            {
                '/',
                '\\'
            });
            if (index < 0)
            {
                return path;
            }

            return path.Substring(index + 1);
        }
    }
}
