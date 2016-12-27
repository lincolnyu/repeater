using PlayListManager;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Foundation.Collections;
using Windows.Storage;
using MediaPlayerCom;

namespace BackgroundAudioTask
{
    /// <summary>
    ///  Impletements IBackgroundTask to provide an entry point for app code to be run in background. 
    ///  Also takes care of handling UVC and communication channel with foreground
    /// </summary>
    public sealed class MyBackgroundAudioTask : IBackgroundTask
    {
        #region Fields

        private SystemMediaTransportControls _systemMediaTransportControl;

        private BackgroundTaskDeferral _deferral; // Used to keep task alive
        
        private ForegroundAppStatus _foregroundAppState = ForegroundAppStatus.Unknown;
        
        private readonly AutoResetEvent _backgroundTaskStarted = new AutoResetEvent(false);
        
        private bool _backgroundTaskRunning;

        private Timer _timer;

        private TimeSpan _lastPosition = TimeSpan.Zero;

        /// <summary>
        ///  If this background task has been preempted
        /// </summary>
        private static bool IsClosed
        {
            get { return BackgroundMediaPlayer.Current.CurrentState == MediaPlayerState.Closed; }
        }

        #endregion

        #region Properties

        /// <summary>
        ///  The play list manager
        /// </summary>
        public MyPlaylistManager PlaylistManager
        {
            get
            {
                return MyPlaylistManager.Instance;
            }
        }

        /// <summary>
        /// Property to hold current playlist
        /// </summary>
        public MyPlayList PlayList
        {
            get
            {
                return PlaylistManager.Current;
            }
        }

        #endregion

        #region IBackgroundTask and IBackgroundTaskInstance Interface Members and handlers

        /// <summary>
        /// The Run method is the entry point of a background task. 
        /// </summary>
        /// <param name="taskInstance"></param>
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            Debug.WriteLine("Background Audio Task " + taskInstance.Task.Name + " starting...");
            // Initialize SMTC object to talk with UVC. 
            //Note that, this is intended to run after app is paused and 
            //hence all the logic must be written to run in background process
            _systemMediaTransportControl = SystemMediaTransportControls.GetForCurrentView();
            _systemMediaTransportControl.ButtonPressed += SystemMediaTransportControlButtonPressed;
            _systemMediaTransportControl.PropertyChanged += SystemMediaTransportControlPropertyChanged;
            _systemMediaTransportControl.IsEnabled = true;
            _systemMediaTransportControl.IsPauseEnabled = true;
            _systemMediaTransportControl.IsPlayEnabled = true;
            _systemMediaTransportControl.IsNextEnabled = true;
            _systemMediaTransportControl.IsPreviousEnabled = true;

            // Associate a cancellation and completed handlers with the background task.
            taskInstance.Canceled += OnCanceled;
            taskInstance.Task.Completed += TaskCompleted;

            var value = ApplicationSettingsHelper.ReadSettingsValue(Constants.AppState);
            if (value == null)
            {
                _foregroundAppState = ForegroundAppStatus.Unknown;
            }
            else
            {
                _foregroundAppState = (ForegroundAppStatus)Enum.Parse(typeof(ForegroundAppStatus), value.ToString());
            }

            //Add handlers for MediaPlayer
            BackgroundMediaPlayer.Current.CurrentStateChanged += Current_CurrentStateChanged;

            //Add handlers for playlist trackchanged
            PlayList.TrackChanged += PlayListTrackChanged;

            //Initialize message channel 
            BackgroundMediaPlayer.MessageReceivedFromForeground += BackgroundMediaPlayerOnMessageReceivedFromForeground;

            //Send information to foreground that background task has been started if app is active
            if (_foregroundAppState != ForegroundAppStatus.Suspended)
            {
                var message = new ValueSet { { Constants.BackgroundTaskStarted, "" } };
                BackgroundMediaPlayer.SendMessageToForeground(message);
            }

            _backgroundTaskStarted.Set();
            _backgroundTaskRunning = true;

            ApplicationSettingsHelper.SaveSettingsValue(Constants.BackgroundTaskState, Constants.BackgroundTaskRunning);
            _deferral = taskInstance.GetDeferral();

            StartTimerIfNot();
        }

        /// <summary>
        /// Indicate that the background task is completed.
        /// </summary>       
        private void TaskCompleted(BackgroundTaskRegistration sender, BackgroundTaskCompletedEventArgs args)
        {
            Debug.WriteLine("MyBackgroundAudioTask " + sender.TaskId + " Completed...");
            _deferral.Complete();
        }

        /// <summary>
        /// Handles background task cancellation. Task cancellation happens due to :
        /// 1. Another Media app comes into foreground and starts playing music 
        /// 2. Resource pressure. Your task is consuming more CPU and memory than allowed.
        /// In either case, save state so that if foreground app resumes it can know where to start.
        /// </summary>
        private void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            // You get some time here to save your state before process and resources are reclaimed
            Debug.WriteLine("MyBackgroundAudioTask " + sender.Task.TaskId + " Cancel Requested...");
            try
            {
                StopTimer();

                //save states
                SavePlayListAndStates();

                ApplicationSettingsHelper.SaveSettingsValue(Constants.BackgroundTaskState, Constants.BackgroundTaskCancelled);
                ApplicationSettingsHelper.SaveSettingsValue(Constants.AppState, Enum.GetName(typeof(ForegroundAppStatus), _foregroundAppState));
                _backgroundTaskRunning = false;
                //unsubscribe event handlers
                _systemMediaTransportControl.ButtonPressed -= SystemMediaTransportControlButtonPressed;
                _systemMediaTransportControl.PropertyChanged -= SystemMediaTransportControlPropertyChanged;
                PlayList.TrackChanged -= PlayListTrackChanged;

                BackgroundMediaPlayer.Current.CurrentStateChanged -= Current_CurrentStateChanged;

                //clear objects task cancellation can happen uninterrupted
                PlaylistManager.ClearPlaylist();

                // presumably the current's state becomes Closed
                BackgroundMediaPlayer.Shutdown(); // shutdown media pipeline

                BackgroundCanceled();
                UpdateUi();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
            _deferral.Complete(); // signals task completion. 
            Debug.WriteLine("MyBackgroundAudioTask Cancel complete...");
        }

        #endregion

        #region SysteMediaTransportControls related functions and handlers

        /// <summary>
        /// Update UVC using SystemMediaTransPortControl apis
        /// </summary>
        private void UpdateUvcOnNewTrack()
        {
            _systemMediaTransportControl.PlaybackStatus = MediaPlaybackStatus.Playing;
            _systemMediaTransportControl.DisplayUpdater.Type = MediaPlaybackType.Music;
            _systemMediaTransportControl.DisplayUpdater.MusicProperties.Title = PlayList.CurrentTrackName;
            _systemMediaTransportControl.DisplayUpdater.Update();
        }

        /// <summary>
        /// Fires when any SystemMediaTransportControl property is changed by system or user
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        void SystemMediaTransportControlPropertyChanged(SystemMediaTransportControls sender, SystemMediaTransportControlsPropertyChangedEventArgs args)
        {
            //TODO: If soundlevel turns to muted, app can choose to pause the music
        }

        /// <summary>
        /// This function controls the button events from UVC.
        /// This code if not run in background process, will not be able to handle button pressed events when app is suspended.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void SystemMediaTransportControlButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Debug.WriteLine("UVC play button pressed");
                    // If music is in paused state, for a period of more than 5 minutes, 
                    //app will get task cancellation and it cannot run code. 
                    //However, user can still play music by pressing play via UVC unless a new app comes in clears UVC.
                    //When this happens, the task gets re-initialized and that is asynchronous and hence the wait
                    if (!_backgroundTaskRunning)
                    {
                        bool result = _backgroundTaskStarted.WaitOne(2000);
                        if (!result)
                        {
                            throw new Exception("Background Task didnt initialize in time");
                        }
                    }
                    StartPlayback();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Pause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    Debug.WriteLine("UVC next button pressed");
                    SkipToNext();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    Debug.WriteLine("UVC previous button pressed");
                    SkipToPrevious();
                    break;
            }
        }


        #endregion

        #region PlayList management functions 

        private void SavePlayListAndStates()
        {
            ApplicationSettingsHelper.SaveSettingsValue(Constants.CurrentTrack, PlayList.CurrentTrackName);
            ApplicationSettingsHelper.SaveSettingsValue(Constants.CurrentTrackIndex, PlayList.CurrentTrackId);
            ApplicationSettingsHelper.SaveSettingsValue(Constants.RepeatMode, PlayList.RepeatMode.ToString());
            ApplicationSettingsHelper.SaveSettingsValue(Constants.Position, BackgroundMediaPlayer.Current.Position.ToString());
            var playListStr = PlayList.ToString();
            ApplicationSettingsHelper.SaveSettingsValue(Constants.PlayList, playListStr);
        }

        private void LoadPlayListAndStates()
        {
            var rmstr = (string)ApplicationSettingsHelper.ReadSettingsValue(Constants.RepeatMode);
            if (rmstr != null)
            {
                var rm = (RepeatModes)Enum.Parse(typeof(RepeatModes), rmstr);
                SetRepeatMode(rm);
            }

            var pl = (string)ApplicationSettingsHelper.ReadSettingsValue(Constants.PlayList);
            if (pl != null)
            {
                PlayList.FromString(pl);
            }
            else
            {
                UpdateUi();
                return;// failed to load the play list
            }
            var tido = ApplicationSettingsHelper.ReadSettingsValue(Constants.CurrentTrackIndex);
            int tid;
            if (tido != null)
            {
                tid = (int)tido;
            }
            else
            {
                //If we dont have anything, play from beginning of playlist.
                PlayList.PlayAllTracks(); //start playback
                UpdateUi();
                return; // failed to load the track index
            }

            var posStr = (string) ApplicationSettingsHelper.ReadSettingsValue(Constants.Position);
            if (posStr != null)
            {
                var ts = TimeSpan.Parse(posStr);
                PlayList.StartTrackAt(tid, ts);
                _lastPosition = TimeSpan.Zero;
            }
            else
            {
                PlayList.StartTrackAt(tid);
            }
            UpdateUi();
        }

        private void Pause()
        {
            Debug.WriteLine("UVC pause button pressed");
            try
            {
                BackgroundMediaPlayer.Current.Pause();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        /// <summary>
        ///  prepares to play the play list (if auto play then it may be auto started)
        /// </summary>
        private void OpenTrack()
        {
            try
            {
                //If we dont have anything, play from beginning of playlist.
                PlayList.PlayAllTracks(); //start playback
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        
        /// <summary>
        /// Start playlist and change UVC state
        /// </summary>
        private void StartPlayback()
        {
            try
            {
                if (!PlayList.AutoPlay || BackgroundMediaPlayer.Current.CurrentState != MediaPlayerState.Playing)
                {
                    BackgroundMediaPlayer.Current.Play();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        /// <summary>
        /// Fires when playlist changes to a new track
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void PlayListTrackChanged(MyPlayList sender, object args)
        {
            UpdateUvcOnNewTrack();
            ApplicationSettingsHelper.SaveSettingsValue(Constants.CurrentTrack, sender.CurrentTrackName);

            if (_foregroundAppState == ForegroundAppStatus.Active)
            {
                //Message channel that can be used to send messages to foreground
                var message = new ValueSet
                {
                    {Constants.TrackChanged, sender.CurrentTrackName},
                    {Constants.CurrentTrackIndex, PlayList.CurrentTrackId}
                };
                BackgroundMediaPlayer.SendMessageToForeground(message);
            }
        }

        /// <summary>
        /// Skip track and update UVC via SMTC
        /// </summary>
        private void SkipToPrevious()
        {
            _systemMediaTransportControl.PlaybackStatus = MediaPlaybackStatus.Changing;
            PlayList.ClickSkipToPrevious();
        }

        /// <summary>
        /// Skip track and update UVC via SMTC
        /// </summary>
        private void SkipToNext()
        {
            _systemMediaTransportControl.PlaybackStatus = MediaPlaybackStatus.Changing;
            PlayList.ClickSkipToNext();
        }

        #endregion

        #region Background Media Player Handlers

        private void Current_CurrentStateChanged(MediaPlayer sender, object args)
        {
            if (sender.CurrentState == MediaPlayerState.Playing)
            {
                _systemMediaTransportControl.PlaybackStatus = MediaPlaybackStatus.Playing;
            }
            else if (sender.CurrentState == MediaPlayerState.Paused)
            {
                _systemMediaTransportControl.PlaybackStatus = MediaPlaybackStatus.Paused;
            }
        }

        /// <summary>
        /// Fires when a message is recieved from the foreground app
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void BackgroundMediaPlayerOnMessageReceivedFromForeground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            foreach (var key in e.Data.Keys)
            {
                switch (key.ToLower())
                {
                    case Constants.Check:
                        Debug.WriteLine("Check");   // the foreground is checking if the background is ready
                        Acknowledge();
                        break;
                    case Constants.AppSuspended:
                        Debug.WriteLine("App suspending"); // App is suspended, you can save your task state at this point
                        _foregroundAppState = ForegroundAppStatus.Suspended;
                        ApplicationSettingsHelper.SaveSettingsValue(Constants.CurrentTrack, PlayList.CurrentTrackName);
                        break;
                    case Constants.AppResumed:
                        Debug.WriteLine("App resuming"); // App is resumed, now subscribe to message channel
                        _foregroundAppState = ForegroundAppStatus.Active;
                        LoadPlayListAndStates();
                        break;
                    case Constants.OpenTrack:
                        Debug.WriteLine("Open track");
                        OpenTrack();
                        break;
                    case Constants.StartPlayback: //Foreground App process has signalled that it is ready for playback
                        Debug.WriteLine("Starting Playback");
                        StartPlayback();
                        break;
                    case Constants.Pause:
                        Debug.WriteLine("Pausing");
                        Pause();
                        break;
                    case Constants.SkipNext: // User has chosen to skip track from app context.
                        Debug.WriteLine("Skipping to next");
                        SkipToNext();
                        break;
                    case Constants.SkipPrevious: // User has chosen to skip track from app context.
                        Debug.WriteLine("Skipping to previous");
                        SkipToPrevious();
                        break;
                    case Constants.ClearTracks:
                        Debug.WriteLine("Clear tracks");
                        PlayList.ClearTracks();
                        break;
                    case Constants.AddTrack:
                        Debug.WriteLine("Add track");
                        await AddTrack((string) e.Data[key]);
                        break;
                    case Constants.RepeatMode:
                        Debug.WriteLine("Repeat mode");
                        var data = (string) e.Data[key];
                        SetRepeatMode((RepeatModes)Enum.Parse(typeof(RepeatModes), data));
                        break;
                    case Constants.CheckBackground:
                        if (IsClosed)
                        {
                            LoadPlayListAndStates();
                        }
                        break;
                }
            }
        }

        /// <summary>
        ///  When background task has been preempted
        /// </summary>
        private void BackgroundCanceled()
        {
            var message = new ValueSet
            {
                {Constants.BackgroundCanceled, ""}
            };
            BackgroundMediaPlayer.SendMessageToForeground(message);
        }

        private void UpdateUi()
        {
            var message = new ValueSet
                {
                    {Constants.TrackChanged, PlayList.CurrentTrackName}, //TODO TrackChanged command should be renamed
                    {Constants.TotalTracks, PlayList.Files.Count}, // NOTE this comes first for the UI is updated upon receiving CurrentTrackId
                    {Constants.CurrentTrackIndex, PlayList.CurrentTrackId},
                    {Constants.RepeatMode, PlayList.RepeatMode.ToString()},
                    {Constants.UpdateUi, "" }
                };
            BackgroundMediaPlayer.SendMessageToForeground(message);
        }

        private void Acknowledge()
        {
            var message = new ValueSet
                {
                    {Constants.BackgroundTaskStarted, "" }
                };
            BackgroundMediaPlayer.SendMessageToForeground(message);
        }

        private async Task AddTrack(string path)
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            PlayList.AddTrack(file);
        }

        private void StartTimerIfNot()
        {
            if (_timer == null)
            {
                _timer = new Timer(MainTimerCallback, null, 0, 200);
            }
        }

        private void StopTimer()
        {
            if (_timer != null)
            {
                _timer.Dispose();
            }
        }

        private void MainTimerCallback(object state)
        {
            var pos = BackgroundMediaPlayer.Current.Position;
            if (pos != _lastPosition)
            {
                UpdateUiSlider();
                
                _lastPosition = pos;
            }
        }

        private void UpdateUiSlider()
        {
            var pos = BackgroundMediaPlayer.Current.Position;
            UpdateUiSlider(pos);
        }

        private void UpdateUiSlider(TimeSpan ts)
        {
            var message = new ValueSet
                {
                    {Constants.CurrentPosition, ts.ToString() }
                };
            BackgroundMediaPlayer.SendMessageToForeground(message);
        }

        private void SetRepeatMode(RepeatModes repeatMode)
        {
            PlayList.RepeatMode = repeatMode;
        }

        #endregion
    }
}
