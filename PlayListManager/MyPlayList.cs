using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Media.Playback;
using Windows.Storage;
using MediaPlayerCom;

namespace PlayListManager
{
   
    /// <summary>
    /// Implement a playlist of tracks. 
    /// If instantiated in background task, it will keep on playing once app is suspended
    /// </summary>
    public sealed class MyPlayList
    {
        #region Fields

        /// <summary>
        ///  The backing field for the list
        /// </summary>
        private readonly List<StorageFile> _files;

        /// <summary>
        ///  To retrieve the file paths without accessing _files which may not be accessible
        /// </summary>
        private readonly List<string> _paths;

        /// <summary>
        ///  The media player
        /// </summary>
        private readonly MediaPlayer _mediaPlayer;

        /// <summary>
        ///  the position to start at for the upcoming new track playback
        /// </summary>
        private TimeSpan _startPosition;
        
        /// <summary>
        ///  If it's to start playing
        /// </summary>
        private bool _toPlay;

        /// <summary>
        ///  If it's playing
        /// </summary>
        private bool _isPlaying;

        /// <summary>
        ///  State since last update
        /// </summary>
        private MediaPlayerState _lastState;

        #endregion

        #region Constructors

        /// <summary>
        ///  Instantiates one
        /// </summary>
        internal MyPlayList()
        {
            _files = new List<StorageFile>();
            _paths = new List<string>();
            CurrentTrackId = -1;
            _mediaPlayer = BackgroundMediaPlayer.Current;
            _mediaPlayer.AutoPlay = false;
            _mediaPlayer.MediaOpened += MediaPlayerMediaOpened;
            _mediaPlayer.MediaEnded += MediaPlayerMediaEnded;
            _lastState = _mediaPlayer.CurrentState;
            _mediaPlayer.CurrentStateChanged += MediaPlayerCurrentStateChanged;
            _mediaPlayer.MediaFailed += MediaPlayerMediaFailed;
        }

        #endregion

        #region Properties

        /// <summary>
        ///  The id of the track currently being played
        /// </summary>
        public int CurrentTrackId { get; set; }

        /// <summary>
        ///  The repeat mode
        /// </summary>
        public RepeatModes RepeatMode { get; set; }

        /// <summary>
        /// Get the current track name
        /// </summary>
        public string CurrentTrackName
        {
            get
            {
                if (CurrentTrackId == -1)
                {
                    return String.Empty;
                }
                if (CurrentTrackId < _files.Count)
                {
                    var path = _files[CurrentTrackId].Path;
                    return path.PathToTrackName();
                }
                // Track Id is higher than total number of tracks
                throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        ///  If to start playing automatically
        /// </summary>
        public bool AutoPlay
        {
            get; set;
        }

        /// <summary>
        ///  The list of files
        /// </summary>
        public IReadOnlyList<StorageFile> Files
        {
            get
            {
                return _files;
            }
        }

        #endregion

        #region Event

        /// <summary>
        /// Invoked when the media player is ready to move to next track
        /// </summary>
        public event TypedEventHandler<MyPlayList, object> TrackChanged;

        #endregion

        #region Methods

        #region object members

        public override string ToString()
        {
            var sb = new StringBuilder();

            foreach (var path in _paths)
            {
                sb.AppendFormat("{0}|", path);
            }
            if (sb.Length > 0)
            {
                sb.Remove(sb.Length - 1, 1);
            }

            return sb.ToString();
        }

        #endregion

        /// <summary>
        ///  Loads tracks from string
        /// </summary>
        /// <param name="s">The string that contains the info of tracks to load</param>
        /// <returns>The async task</returns>
        public IAsyncAction FromString(string s)
        {
            return _FromString(s).AsAsyncAction();
        }

        /// <summary>
        ///  Loads tracks from string
        /// </summary>
        /// <param name="s">The string that contains the info of tracks to load</param>
        /// <returns>The async task</returns>
        private async Task _FromString(string s)
        {
            var segs = s.Split('|');
            ClearTracks();
            foreach (var path in segs)
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                AddTrack(file);
            }
        }

        #region MediaPlayer Handlers

        /// <summary>
        /// Handler for state changed event of Media Player
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private void MediaPlayerCurrentStateChanged(MediaPlayer sender, object args)
        {
            if (_lastState == MediaPlayerState.Closed || _lastState == MediaPlayerState.Opening 
                || _lastState == MediaPlayerState.Buffering)
            {
                // sets initial position
                sender.Position = _startPosition;
            }

            if (sender.CurrentState == MediaPlayerState.Playing)
            {
                //sender.Volume = 1.0;
                sender.PlaybackMediaMarkers.Clear();
                _isPlaying = true;
            }
            if (sender.CurrentState == MediaPlayerState.Paused)
            {
                _isPlaying = false;
            }
            else
            {
                _isPlaying = true;
            }

            _lastState = sender.CurrentState;
        }

        /// <summary>
        /// Fired when MediaPlayer is ready to play the track
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private void MediaPlayerMediaOpened(MediaPlayer sender, object args)
        {
            if (AutoPlay || _toPlay)
            {
                // wait for media to be ready
                sender.Play();
                _toPlay = false;
            }
            Debug.WriteLine("New Track" + CurrentTrackName);
            Debug.Assert(TrackChanged != null, "TrackChanged != null");
            if (TrackChanged != null)
            {
                TrackChanged.Invoke(this, CurrentTrackName);
            }
        }

        /// <summary>
        ///  Handler for MediaPlayer Media Ended
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private void MediaPlayerMediaEnded(MediaPlayer sender, object args)
        {
            switch (RepeatMode)
            {
                case RepeatModes.RepeatSingle:
                    _toPlay = true;
                    Replay();
                    break;
                case RepeatModes.RepeatAll:
                    _toPlay = true;
                    LoopToNext();
                    break;
                case RepeatModes.PlayAll:
                    _toPlay = true;
                    if (!SkipToNext())
                    {
                        _mediaPlayer.Pause();
                    }
                    break;
                case RepeatModes.PlaySingle:
                    _mediaPlayer.Pause();
                    break;
            }
        }

        /// <summary>
        ///  Media player failed
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private void MediaPlayerMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            Debug.WriteLine("Failed with error code " + args.ExtendedErrorCode);
        }

        #endregion

        /// <summary>
        ///  Adds a track
        /// </summary>
        /// <param name="file">The track to add</param>
        public void AddTrack(StorageFile file)
        {
            _files.Add(file);
            _paths.Add(file.Path);
        }

        /// <summary>
        ///  Adds a track to the specified position
        /// </summary>
        /// <param name="index">The index to add at</param>
        /// <param name="file">The track</param>
        public void AddTrack(int index, StorageFile file)
        {
            _files.Insert(index, file);
            _paths.Insert(index, file.Path);
        }

        /// <summary>
        ///  Removes the specified track
        /// </summary>
        /// <param name="index">The index of the track</param>
        [DefaultOverload]
        public void RemoveTrack(int index)
        {
            _files.RemoveAt(index);
            _paths.RemoveAt(index);
        }

        /// <summary>
        ///  Removes the specified track
        /// </summary>
        /// <param name="file">The track to remove</param>
        public void RemoveTrack(StorageFile file)
        {
            _files.Remove(file);
            _paths.Remove(file.Path);
        }

        /// <summary>
        ///  Clears all tracks
        /// </summary>
        public void ClearTracks()
        {
            _files.Clear();
            _paths.Clear();
        }

        /// <summary>
        /// Starts track at given position in the track list
        /// </summary>
        public void StartTrackAt(int id)
        {
            StartTrackAt(id, TimeSpan.Zero);
        }

        /// <summary>
        ///  Starts playing the track at the specified position
        /// </summary>
        /// <param name="id">The ID of the track</param>
        /// <param name="position">The position to start at</param>
        public void StartTrackAt(int id, TimeSpan position)
        {
            CurrentTrackId = id;

            // Set the start position, we set the position once the state changes to playing, 
            // it can be possible for a fraction of second, playback can start before we are 
            // able to seek to new start position
            _startPosition = position;
            _mediaPlayer.SetFileSource(_files[id]);
        }

        /// <summary>
        ///  Starts playing the track at the specified position
        /// </summary>
        /// <param name="id"></param>
        /// <param name="position"></param>
        /// <param name="initVolume"></param>
        public void StartTrackAt(int id, TimeSpan position, double initVolume)
        {
            CurrentTrackId = id;

            // Set the start position, we set the position once the state changes to playing, 
            // it can be possible for a fraction of second, playback can start before we are 
            // able to seek to new start position
            _mediaPlayer.Volume = initVolume;
            _startPosition = position;
            _mediaPlayer.SetFileSource(_files[id]);
        }

        /// <summary>
        /// Starts a given track by finding its name
        /// </summary>
        [DefaultOverload]
        public void StartTrackAt(string trackName)
        {
            for (int i = 0; i < _files.Count; i++)
            {
                if (TrackNameMatchesPath(_files[i].Path, trackName))
                {
                    StartTrackAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// Starts a given track by finding its name and at desired position
        /// </summary>
        [DefaultOverload]
        public void StartTrackAt(string trackName, TimeSpan position)
        {
            for (int i = 0; i < _files.Count; i++)
            {
                if (TrackNameMatchesPath(_files[i].Path, trackName))
                {
                    StartTrackAt(i, position);
                    break;
                }
            }
        }

        /// <summary>
        /// Play all tracks in the list starting with 0 
        /// </summary>
        public void PlayAllTracks()
        {
            StartTrackAt(0);
        }

        /// <summary>
        ///  Rewind to zero and replay
        /// </summary>
        public void Replay()
        {
            _mediaPlayer.Position = TimeSpan.Zero;
        }

        /// <summary>
        ///  Loops to next
        /// </summary>
        private void LoopToNext()
        {
            StartTrackAt((CurrentTrackId + 1) % _files.Count);
        }

        /// <summary>
        /// Skip to next track
        /// </summary>
        private bool SkipToNext()
        {
            var canPlay = CurrentTrackId < _files.Count-1;
            if (canPlay)
            {
                LoopToNext();
            }
            return canPlay;
        }

        /// <summary>
        ///  Loops to previous
        /// </summary>
        private void LoopToPrevous()
        {
            if (CurrentTrackId == 0)
            {
                StartTrackAt(CurrentTrackId);
            }
            else
            {
                StartTrackAt(CurrentTrackId - 1);
            }
        }

        /// <summary>
        /// Skip to next track
        /// </summary>
        private bool SkipToPrevious()
        {
            var canPlay = CurrentTrackId > 0;
            if (canPlay)
            {
                LoopToPrevous();
            }
            return canPlay;
        }

        /// <summary>
        ///  Responds to skip to next
        /// </summary>
        /// <returns>True if can</returns>
        public bool ClickSkipToNext()
        {
            _toPlay = _isPlaying;
            return SkipToNext();
        }

        /// <summary>
        ///  Responds to skip to previous
        /// </summary>
        /// <returns>True if can</returns>
        public bool ClickSkipToPrevious()
        {
            _toPlay = _isPlaying;
            if (_mediaPlayer.Position > TimeSpan.FromMilliseconds(1000)) // demarcate rewind to beginning and skip to previous
            {
                Replay();
                return true;
            }
            return SkipToPrevious();
        }

        /// <summary>
        ///  if the track name is consistent with the path (file name)
        /// </summary>
        /// <param name="path">The path</param>
        /// <param name="trackName">The track name</param>
        /// <returns>True if the track name is consistent with the path</returns>
        private static bool TrackNameMatchesPath(string path, string trackName)
        {
            var test = path.Split('/')[path.Split('/').Length - 1];
            return string.Equals(test, trackName, StringComparison.CurrentCultureIgnoreCase);
        }

        #endregion
    }
}
