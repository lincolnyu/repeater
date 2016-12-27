namespace PlayListManager
{
    /// <summary>
    /// Manage playlist information. For simplicity of this sample, we allow only one playlist
    /// </summary>
    public sealed class MyPlaylistManager
    {
        #region Fields
        
        private MyPlayList _currentList;

        private static MyPlaylistManager _instance;

        #endregion

        #region Properties

        public static MyPlaylistManager Instance
        {
            get
            {
                return _instance ?? (_instance = new MyPlaylistManager());
            }
        }

        public MyPlayList Current
        {
            get
            {
                return _currentList ?? (_currentList = new MyPlayList());
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Clears playlist for re-initialization
        /// </summary>
        public void ClearPlaylist()
        {
            _currentList = null;
        }

        #endregion
    }
}
