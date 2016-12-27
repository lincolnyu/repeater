using System;
#if DEBUG
using System.Collections.Specialized;
#endif
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Playback;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;
using MediaPlayerCom;
using PlayListManager;
using Repeater.Annotations;
using Repeater.Converters;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkID=390556

namespace Repeater
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : INotifyPropertyChanged, IFileOpenPickerContinuable
    {
        #region Fields

        /// <summary>
        ///  Backing field for CurrentRepeatMode
        /// </summary>
        private RepeatModes _currentRepeatMode;

        /// <summary>
        ///  Backing field for RemaininTime
        /// </summary>
        private TimeSpan _remainingTime;

        /// <summary>
        ///  Backing field for PlayTime
        /// </summary>
        private TimeSpan _playTime;

        /// <summary>
        ///  Backing field for PickedPath
        /// </summary>
        private string _pickedPath = "";

        /// <summary>
        ///  If the repeat mode is to be sent when the background is available
        /// </summary>
        private bool _repeatModeToBeSent;// this is to overwrite the mode read from resuming
        
        /// <summary>
        ///  Prevent recursive repeat mode sending
        /// </summary>
        private bool _suppressRepeatModeSending;

        /// <summary>
        ///  The last pause position
        /// </summary>
        private TimeSpan _lastPausePosition;

        /// <summary>
        ///  Saved pause position where repeated play should rewind to
        /// </summary>
        private TimeSpan _savedPausePosition;

        /// <summary>
        ///  Point where repeated play should rewind
        /// </summary>
        private TimeSpan _endPosition;

        /// <summary>
        ///  If it's currently doing a repeated play
        /// </summary>
        private bool _repeating;

        /// <summary>
        ///  Debugger only used in debug mode
        /// </summary>
        private readonly Debugger _debugger = new Debugger();

        /// <summary>
        ///  Event for background check and acknowledge communication
        /// </summary>
        private readonly AutoResetEvent _sererInitialized;

        /// <summary>
        ///  Backing caching field for IsMyBackgroundTaskRunning to check before inquires the background
        /// </summary>
        private bool _isMyBackgroundTaskRunning;

        /// <summary>
        ///  Number of total tracks
        /// </summary>
        private int _fileNum;

        /// <summary>
        ///  If playback UI is enabled
        /// </summary>
        private bool _playbackUiEnabled;

        /// <summary>
        ///  The index of the current track in the play list
        /// </summary>
        private int _currentTrackIndex = -1;

        /// <summary>
        ///  To prevent recursive update of current position
        /// </summary>
        private bool _suppressSliderChangeResponse;

        /// <summary>
        ///  If media events have been subscribed to
        /// </summary>
        private bool _mediaEventsSubscribed;

        /// <summary>
        ///  If app events have been subscribed to
        /// </summary>
        private bool _appEventSubscribed;

        /// <summary>
        ///  The reference to the instance of the current main page for outside user to use
        /// </summary>
        public static MainPage Current;

        #endregion

        #region Constructors

        /// <summary>
        ///  Instantiates the page
        /// </summary>
        public MainPage()
        {
            InitializeComponent();

            DataContext = this;

            Current = this;

            _sererInitialized = new AutoResetEvent(false);

#if DEBUG
            _debugger.Messages.CollectionChanged += MessagesOnCollectionChanged;
            LstMessages.Visibility = Visibility.Visible;
#else
            LstMessages.Visibility = Visibility.Collapsed;
#endif

            BtnOpenFile.Click += BtnOpenFileOnClick;
            BtnPlay.Click += BtnPlayOnClick;
            BtnBackRepeat.Click += BtnBackRepeatOnClick;
            BtnStopRepeat.Click += BtnStopRepeatOnClick;
            BtnGoBack.Click += BtnGoBackOnClick;
            BtnGoForward.Click += BtnGoForwardOnClick;
            BtnPrev.Click += BtnPrevOnClick;
            BtnNext.Click += BtnNextOnClick;
            BtnBeginning.Click += BtnBeginningClick;

            PlaySlider.ValueChanged += PlaySliderOnValueChanged;

            UpdateUiAsPerState();

            InitRepeatingUi();
            ResetRepeaterPointers();
        }

        #endregion

        #region Properties

        /// <summary>
        ///  The media the background media player is currently playing
        /// </summary>
        private MediaPlayer Media
        {
            get
            {
                return BackgroundMediaPlayer.Current;
            }
        }


        #region UI bound properties

        /// <summary>
        ///  All possible repeat modes
        /// </summary>
        public string[] AllRepeatModes 
        {
            get
            {
                var names = Enum.GetNames(typeof(RepeatModes));
                var results = new string[names.Length];
                var i = 0;
                foreach (var ss in names.Select(RepeatModeToStringConverter.SplitString))
                {
                    results[i] = ss;
                    i++;
                }
                return results;
            }
        }

        /// <summary>
        ///  Current repeat mode
        /// </summary>
        public RepeatModes CurrentRepeatMode
        {
            get { return _currentRepeatMode; }
            set
            {
                if (_currentRepeatMode != value)
                {
                    _currentRepeatMode = value;
                    UpdateRepeatModeToBackground();
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        ///  Current track path
        /// </summary>
        public string PickedPath
        {
            get { return _pickedPath; }
            set
            {
                if (_pickedPath != value)
                {
                    _pickedPath = value;
                    OnPropertyChanged();
// ReSharper disable once ExplicitCallerInfoArgument
                    OnPropertyChanged("PickedTrack");
                }
            }
        }

        /// <summary>
        ///  Current track name
        /// </summary>
        public string PickedTrack
        {
            get { return PickedPath.PathToTrackName(); }
        }

        /// <summary>
        ///  Total media duration
        /// </summary>
        public TimeSpan MediaDuration
        {
            get
            {
                return Media.NaturalDuration;
            }
        }

        /// <summary>
        ///  The time that has elapsed to display
        /// </summary>
        public TimeSpan PlayTime
        {
            get { return _playTime; }
            set
            {
                if (_playTime != value)
                {
                    _playTime = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        ///  The remaining time to display
        /// </summary>
        public TimeSpan RemainingTime
        {
            get { return _remainingTime; }
            set
            {
                if (_remainingTime != value)
                {
                    _remainingTime = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        /// <summary>
        /// Gets the information about background task is running or not by reading the setting saved by background task
        /// </summary>
        private bool IsMyBackgroundTaskRunning
        {
            get
            {
                if (_isMyBackgroundTaskRunning)
                {
                    return true;
                }

                try
                {
                    var value = ApplicationSettingsHelper.ReadSettingsValue(Constants.BackgroundTaskState);
                    if (value == null)
                    {
                        return false;
                    }

                    _isMyBackgroundTaskRunning = ((string) value).Equals(Constants.BackgroundTaskRunning);
                    return _isMyBackgroundTaskRunning;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }


        #endregion

        #region Events

        #region INotifyPropertyChanged members

        /// <summary>
        ///  Property changed event to the UI
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #endregion

        #region Methods

        #region INotifyPropertyChanged members

        /// <summary>
        ///  Raise property changed event
        /// </summary>
        /// <param name="propertyName">The name of the property that has changed</param>
        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        #endregion

        #region IFileOpenPickerContinuable members

        /// <summary>
        /// Handle the returned files from file picker
        /// This method is triggered by ContinuationManager based on ActivationKind
        /// </summary>
        /// <param name="args">File open picker continuation activation argment. It cantains the list of files user selected with file open picker </param>
        public async void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args)
        {
            await StartBackgroundAudioTaskIfNot();

            _debugger.WriteMessageFormat("Clearing");
            ClearTracks();
            foreach (var file in args.Files)
            {
                StorageApplicationPermissions.FutureAccessList.Add(file);
                _debugger.WriteMessageFormat("Adding track {0}", file.Path);
                AddTrack(file.Path);
            }

            _fileNum = args.Files.Count;
            _currentTrackIndex = _fileNum > 0 ? 0 : -1;

            _debugger.WriteMessage("Starting background track");

            _debugger.WriteMessageFormat("IsMyBackgroundTaskRunning = {0}", IsMyBackgroundTaskRunning);

            UpdateRepeatModeToBackground();

            StartBackgroundAudioTrack();

            UpdateUiAsPerState();
        }

        #endregion

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="args">
        ///  Event data that describes how this page was reached.
        ///  This parameter is typically used to configure the page.
        /// </param>
        protected override void OnNavigatedTo(NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);

            //SuspensionManager.RegisterFrame(Frame, "MainPageFrame");
            SubscribeAppEvents();

            TryReconnectingBackground(); //check if in this case it should send AppResumed to background as well
        }

        /// <summary>
        ///  On navigated from this app
        /// </summary>
        /// <param name="args">The event args</param>
        /// <remarks>
        ///  TODO investigate
        ///  TODO it seems this is neither called when the app is switched from nor when the app quits
        /// </remarks>
        protected override void OnNavigatedFrom(NavigationEventArgs args)
        {
            //SuspensionManager.UnregisterFrame(Frame);

            UnsubscribeMediaPlayerEventHandlers();

            UnsubscribeAppEvents();
        
            base.OnNavigatedFrom(args);
        }


        /// <summary>
        ///  Tries reconnect to the background for resuming
        /// </summary>
        private void TryReconnectingBackground()
        {
            ApplicationSettingsHelper.SaveSettingsValue(Constants.AppState, Constants.ForegroundAppActive);

            // Verify if the task was running before
            if (IsMyBackgroundTaskRunning)
            {
                //if yes, reconnect to media play handlers
                SubscribeMediaPlayerEventHandlers();

                Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>
                    {
                        try
                        {
                            PlaySlider.Maximum = MediaDuration.TotalMilliseconds;// is this needed? maybe this doesn't have to be passed from the background

                            //send message to background task that app is resumed, so it can start sending notifications
                            var messageDictionary = new ValueSet
                            {
                                {Constants.AppResumed, DateTime.Now.ToString()}
                            };

                            BackgroundMediaPlayer.SendMessageToBackground(messageDictionary);
                        }
                        catch (Exception e)
                        {
                            _debugger.Messages.Add(string.Format("Error reconnecting background {0}", e.Message));
                        }
                    });
                _debugger.Messages.Add("Restored");
            }
            else
            {
                _debugger.Messages.Add("Failed to restore");
            }

            UpdateUiAsPerState();
        }

        #region Foreground App Lifecycle Handlers

        /// <summary>
        ///  Subscribes to app events
        /// </summary>
        private void SubscribeAppEvents()
        {
            if (!_appEventSubscribed)
            {
                Application.Current.Suspending += ForegroundAppSuspending;
                Application.Current.Resuming += ForegroundAppResuming;
                _appEventSubscribed = true;
            }
        }

        /// <summary>
        ///  Unsubscribe to app events
        /// </summary>
        private void UnsubscribeAppEvents()
        {
            if (_appEventSubscribed)
            {
                Application.Current.Suspending -= ForegroundAppSuspending;
                Application.Current.Resuming -= ForegroundAppResuming;
                _appEventSubscribed = false;
            }
        }

        /// <summary>
        ///  Sends message to background informing app has resumed
        ///  Subscribe to MediaPlayer events
        /// </summary>
        /// <param name="sender">The sender of teh event</param>
        /// <param name="args">The args of the event</param>
        private void ForegroundAppResuming(object sender, object args)
        {
            //ResetRepeaterPointers();
            TryReconnectingBackground();
        }

        /// <summary>
        ///  Send message to Background process that app is to be suspended
        ///  Stop clock and slider when suspending
        ///  Unsubscribe handlers for MediaPlayer events
        /// </summary>
        /// <param name="sender">The sender of teh event</param>
        /// <param name="args">The args of the event</param>
        private void ForegroundAppSuspending(object sender, SuspendingEventArgs args)
        {
            var deferral = args.SuspendingOperation.GetDeferral();

            if (IsMyBackgroundTaskRunning)
            {
                var messageDictionary = new ValueSet
                    {
                        {Constants.AppSuspended, DateTime.Now.ToString()}
                    };
                BackgroundMediaPlayer.SendMessageToBackground(messageDictionary);
            }
            UnsubscribeMediaPlayerEventHandlers();

            ApplicationSettingsHelper.SaveSettingsValue(Constants.AppState, Constants.ForegroundAppSuspended);
            deferral.Complete();
        }

        #endregion

        #region UI event handlers


        /// <summary>
        ///  Open file button clicked
        /// </summary>
        /// <param name="sender">The sender of teh event</param>
        /// <param name="args">The args of the event</param>
        private void BtnOpenFileOnClick(object sender, RoutedEventArgs args)
        {
            var openPicker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
            };
            openPicker.FileTypeFilter.Add(".mp3");
            openPicker.FileTypeFilter.Add(".wav");

            // Launch file open picker and caller app is suspended and may be terminated if required
            openPicker.PickMultipleFilesAndContinue();
        }

        /// <summary>
        ///  Play button clicked
        /// </summary>
        /// <param name="sender">The sender of teh event</param>
        /// <param name="args">The args of the event</param>
        private async void BtnPlayOnClick(object sender, RoutedEventArgs args)
        {
            await StartBackgroundAudioTaskIfNot();

            switch (Media.CurrentState)
            {
                case MediaPlayerState.Playing:
                    PauseMedia();
                    break;
                default:
                    if (!_repeating)
                    {
                        _savedPausePosition = _lastPausePosition;
                    }
                    PlayMedia();
                    break;
            }
        }

        /// <summary>
        ///  Go forward button clicked
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private async void BtnGoForwardOnClick(object sender, RoutedEventArgs args)
        {
            if (_repeating)
            {
                StopRepeating();
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var val = Media.Position;
                val += TimeSpan.FromSeconds(5);
                if (val >= MediaDuration)
                {
                    val = MediaDuration;
                }
                Media.Position = val;
            });
        }

        /// <summary>
        ///  Go back button clicked
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private async void BtnGoBackOnClick(object sender, RoutedEventArgs args)
        {
            if (_repeating)
            {
                StopRepeating();
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var val = Media.Position;
                val -= TimeSpan.FromSeconds(5);
                if (val < TimeSpan.Zero)
                {
                    val = TimeSpan.Zero;
                }
                Media.Position = val;
            });
        }

        /// <summary>
        ///  Go next button clicked
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private async void BtnNextOnClick(object sender, RoutedEventArgs args)
        {
            var message = new ValueSet { { Constants.SkipNext, "0" } };
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                BackgroundMediaPlayer.SendMessageToBackground(message);
            });
        }

        /// <summary>
        ///  Go previous button clicked
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private async void BtnPrevOnClick(object sender, RoutedEventArgs args)
        {
            var message = new ValueSet { { Constants.SkipPrevious, "0" } };
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                BackgroundMediaPlayer.SendMessageToBackground(message);
            });
        }

        /// <summary>
        ///  Go to the beginning button clicked
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private void BtnBeginningClick(object sender, RoutedEventArgs args)
        {
            StartBackgroundAudioTrack();
        }

        /// <summary>
        ///  Play slider's value has changed
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private async void PlaySliderOnValueChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            if (_suppressSliderChangeResponse)
            {
                return;
            }
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var ms = PlaySlider.Value;
                var request = TimeSpan.FromMilliseconds(ms);
                Media.Position = request > MediaDuration ? MediaDuration : request;
            });
        }

        /// <summary>
        ///  Back to repeat button clicked
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private async void BtnBackRepeatOnClick(object sender, RoutedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                _endPosition = Media.Position;
                Media.Position = _savedPausePosition;
                if (Media.CurrentState != MediaPlayerState.Playing)
                {
                    PlayMedia();
                }
                StartRepeating();
            });
        }

        /// <summary>
        ///  Stop repeating button clicked
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The args of the event</param>
        private void BtnStopRepeatOnClick(object sender, RoutedEventArgs args)
        {
            StopRepeating();
        }

        #endregion

        #region Media event handlers and their subscriptions

        /// <summary>
        /// Subscribes to MediaPlayer events
        /// </summary>
        private void SubscribeMediaPlayerEventHandlers()
        {
            if (!_mediaEventsSubscribed)
            {
                Media.CurrentStateChanged += MediaOnCurrentStateChanged;
                Media.MediaFailed += MediaOnMediaFailed;
                Media.MediaOpened += MediaOnMediaOpened;
                Media.MediaEnded += MediaOnMediaEnded;
                BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayerOnMessageReceivedFromBackground;
                _mediaEventsSubscribed = true;
            }
        }

        /// <summary>
        /// Unsubscribes to MediaPlayer events. Should run only on suspend
        /// </summary>
        private void UnsubscribeMediaPlayerEventHandlers()
        {
            if (_mediaEventsSubscribed)
            {
                Media.CurrentStateChanged -= MediaOnCurrentStateChanged;
                Media.MediaFailed -= MediaOnMediaFailed;
                Media.MediaOpened -= MediaOnMediaOpened;
                Media.MediaEnded -= MediaOnMediaEnded;
                BackgroundMediaPlayer.MessageReceivedFromBackground -= BackgroundMediaPlayerOnMessageReceivedFromBackground;
                _mediaEventsSubscribed = false;
            }
        }

        private void Resubscribe()
        {
            UnsubscribeMediaPlayerEventHandlers();
            SubscribeMediaPlayerEventHandlers();
        }

        /// <summary>
        ///  Media opened
        /// </summary>
        /// <param name="sender">The sender of teh event</param>
        /// <param name="args">The args of the event</param>
        private void MediaOnMediaOpened(MediaPlayer sender, object args)
        {
            // Assuming this happens when new track is opened and therefore repeater pointers should be reset
            ResetRepeaterPointers();
            Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    PlaySlider.Maximum = MediaDuration.TotalMilliseconds;
                });
        }

        /// <summary>
        ///  When media has failed
        /// </summary>
        /// <param name="sender">The sender of teh event</param>
        /// <param name="e">The args of the event</param>
        private void MediaOnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs e)
        {
            // TODO do some thing (restart the background?)...
            _debugger.WriteMessageFormat("Media failed due to '{0}'\n", e.ErrorMessage);
        }

        /// <summary>
        ///  When media has ended
        /// </summary>
        /// <param name="sender">The sender of teh event</param>
        /// <param name="args">The args of the event</param>
        private void MediaOnMediaEnded(MediaPlayer sender, object args)
        {
        }

        #endregion

        #region Time management

        /// <summary>
        ///  Updates the current play time to the UI
        /// </summary>
        /// <param name="playTime">The elapsed time</param>
        private void SetPlayTime(TimeSpan playTime)
        {
            PlayTime = playTime;
            RemainingTime = MediaDuration - playTime;
        }


        /// <summary>
        ///  Resets all time parameters
        /// </summary>
        private void ResetRepeaterPointers()
        {
            _endPosition = _lastPausePosition = _savedPausePosition = TimeSpan.Zero;
        }

        #endregion

        #region Messaging with background

        /// <summary>
        ///  Call StartBackgroundAudioTask if the background task is not known to the UI to be running or the media is closed
        /// </summary>
        /// <returns></returns>
        private async Task StartBackgroundAudioTaskIfNot()
        {
            if (!IsMyBackgroundTaskRunning || Media.CurrentState == MediaPlayerState.Closed)
            {
                _debugger.Messages.Add(string.Format("(MyBgt {0}, MCS {1}", IsMyBackgroundTaskRunning, Media.CurrentState));
                await StartBackgroundAudioTask();
            }
        }

        /// <summary>
        ///  Sends a signal to the background audio task to get it ready
        /// </summary>
        /// <returns>The async task</returns>
        private async Task StartBackgroundAudioTask()
        {
            SubscribeMediaPlayerEventHandlers();

            var done = false;
            while (!done)
            {
                _debugger.WriteMessageFormat("Starting background audio");
                var message = new ValueSet
                {
                    {Constants.Check, "" }
                };
                BackgroundMediaPlayer.SendMessageToBackground(message);

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>
                    {
                        var result = _sererInitialized.WaitOne(1000);
                        //Send message to initiate playback

                        done = result;
                        if (!result)
                        {
                            _debugger.Messages.Add("Background Audio task hasn't started in due time");
                           // throw new Exception("Background Audio Task didn't start in expected time");
                        }
                    });  
            }
            _debugger.Messages.Add("Background Audio task started successfully");
        }

        /// <summary>
        ///  Starts a background audio track
        /// </summary>
        private void StartBackgroundAudioTrack()
        {
            var message = new ValueSet {{Constants.OpenTrack, "0"}};
            BackgroundMediaPlayer.SendMessageToBackground(message);
        }

        /// <summary>
        ///  Updates the repeat mode in the UI to the background 
        /// </summary>
        private void UpdateRepeatModeToBackground()
        {
            if (_suppressRepeatModeSending)
            {
                return;
            }
            if (IsMyBackgroundTaskRunning)
            {
                // it's running send the message straight away and don't wait for and accept the udpate from the background
                var message = new ValueSet {{Constants.RepeatMode, CurrentRepeatMode.ToString()}};
                BackgroundMediaPlayer.SendMessageToBackground(message);
                _repeatModeToBeSent = false;
            }
            else
            {
                _repeatModeToBeSent = true;
            }
        }

        /// <summary>
        ///  Clears all tracks
        /// </summary>
        private void ClearTracks()
        {
            var message = new ValueSet {{Constants.ClearTracks, "0"}};
            BackgroundMediaPlayer.SendMessageToBackground(message);
        }

        /// <summary>
        ///  Adds a track with the specified path
        /// </summary>
        /// <param name="path">The path to the audio</param>
        private void AddTrack(string path)
        {
            var message = new ValueSet {{Constants.AddTrack, path}};
            BackgroundMediaPlayer.SendMessageToBackground(message);
        }

        /// <summary>
        ///  Starts playback
        /// </summary>
        private void StartBackgroundAudioPlayback()
        {
            var message = new ValueSet {{Constants.StartPlayback, "0"}};
            BackgroundMediaPlayer.SendMessageToBackground(message);
        }

        /// <summary>
        ///  Pauses the playback
        /// </summary>
        private void Pause()
        {
            var message = new ValueSet { { Constants.Pause, "0" } };
            BackgroundMediaPlayer.SendMessageToBackground(message);
        }

        /// <summary>
        ///  Calls background to see if it's been preempted and requires reloading
        /// </summary>
        public void CheckBackground()
        {
            Resubscribe();
            var message = new ValueSet { { Constants.CheckBackground, "0" } };
            BackgroundMediaPlayer.SendMessageToBackground(message);
        }

        /// <summary>
        /// This event fired when a message is recieved from Background Process
        /// </summary>
        private async void BackgroundMediaPlayerOnMessageReceivedFromBackground(object sender,
            MediaPlayerDataReceivedEventArgs e)
        {
            foreach (var key in e.Data.Keys)
            {
                var key1 = key;
                switch (key)
                {
                    case Constants.TrackChanged:
                        //When foreground app is active change track based on background message
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            PickedPath = (string) e.Data[key1];
                        });
                        break;
                    case Constants.CurrentTrackIndex:
                        _currentTrackIndex = (int) e.Data[key];
                        _debugger.Messages.Add(string.Format("Index set to {0}", _currentTrackIndex));
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            EnablePlaybackUi(_playbackUiEnabled);
                        });
                        break;
                    case Constants.TotalTracks:
                        _fileNum = (int) e.Data[key];
                        _debugger.Messages.Add(string.Format("FileNum set to {0}", _fileNum));
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            EnablePlaybackUi(_playbackUiEnabled);
                        });
                        break;
                    case Constants.RepeatMode:
                        // if the repeat mode is not waiting to be sent down to the background
                        // then accept the mode update from the background
                        if (!_repeatModeToBeSent)
                        {
                            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                _suppressRepeatModeSending = true;
                                var srm = (string) e.Data[key1];
                                var rm = (RepeatModes) Enum.Parse(typeof (RepeatModes), srm);
                                CurrentRepeatMode = rm;
                                _suppressRepeatModeSending = false;
                            });
                        }
                        break;
                    case Constants.BackgroundTaskStarted:
                        //Wait for Background Task to be initialized before starting playback
                        Debug.WriteLine("Background Task started");
                        _sererInitialized.Set();
                        break;
                    case Constants.CurrentPosition:
                    {
                        var currpos = TimeSpan.Parse((string) e.Data[key]);
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            if (_repeating && currpos >= _endPosition && _endPosition > _savedPausePosition)
                            {
                                Media.Position = _savedPausePosition;
                            }
                            if (currpos.TotalMilliseconds >= 0 && currpos.TotalMilliseconds <= PlaySlider.Maximum)
                            {
                                _suppressSliderChangeResponse = true;
                                PlaySlider.Value = currpos.TotalMilliseconds;
                                UpdateBar(currpos, true);
                                SetPlayTime(currpos);
                                _suppressSliderChangeResponse = false;
                            }
                            BtnBackRepeat.IsEnabled = currpos > _savedPausePosition;
                        });
                        break;
                    }
                    case Constants.UpdateUi:
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, UpdateUiAsPerState);
                        break;
                    case Constants.BackgroundCanceled:
                        _isMyBackgroundTaskRunning = false;
                        break;
                }
            }
        }

        #endregion

        #region UI update

        /// <summary>
        ///  When media's current state has changed
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="args">The arguments of the event</param>
        private async void MediaOnCurrentStateChanged(MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                _debugger.WriteMessageFormat("2> StateChanged to {0}\n", Media.CurrentState.ToString());
                UpdateUiAsPerState();
            });
        }

        /// <summary>
        ///  Updates the UI according to the current media play state
        /// </summary>
        public void UpdateUiAsPerState()
        {
            if (IsMyBackgroundTaskRunning)
            {
                switch (Media.CurrentState)
                {
                    case MediaPlayerState.Playing:
                        BtnPlay.Content = "Pause";
                        EnablePlaybackUi(true);
                        InitEnableRepeating();
                        break;
                    case MediaPlayerState.Paused:
                    case MediaPlayerState.Stopped:
                        BtnPlay.Content = "Play";
                        EnablePlaybackUi(true);
                        break;
                    default:
                        BtnPlay.Content = "Play";
                        EnablePlaybackUi(false);
                        break;
                }
            }
            else
            {
                BtnPlay.Content = "Play";
                EnablePlaybackUi(false);
            }
        }

        /// <summary>
        ///  Enables playback UI
        /// </summary>
        /// <param name="enable">if to enable</param>
        private void EnablePlaybackUi(bool enable)
        {
            _playbackUiEnabled = enable;
            BtnPlay.IsEnabled = enable;
            BtnGoBack.IsEnabled = enable;
            BtnGoForward.IsEnabled = enable;
            PlaySlider.IsEnabled = enable;
            BtnBackRepeat.IsEnabled = false;
            BtnStopRepeat.IsEnabled = false;
            BtnPrev.IsEnabled = enable && _currentTrackIndex >= 0;
            BtnNext.IsEnabled = enable && _currentTrackIndex < _fileNum-1;
            BtnBeginning.IsEnabled = enable && _currentTrackIndex >= 0;
        }

        private void InitRepeatingUi()
        {
            BtnBackRepeat.IsEnabled = false;
            BtnStopRepeat.IsEnabled = false;
        }

        private void InitEnableRepeating()
        {
            //BtnBackRepeat.IsEnabled = !_repeating;
            BtnStopRepeat.IsEnabled = _repeating;
        }

        private void UpdateBar(TimeSpan currpos, bool visible)
        {
            if (visible)
            {
                var pos = PlaySlider.TransformToVisual(MainCanvas).TransformPoint(new Point(0, 0));
                var height = PlaySlider.ActualHeight;

                var len = PlaySlider.ActualWidth;

                double x2;
                if (_repeating)
                {
                    x2 = len * _endPosition.TotalMilliseconds / MediaDuration.TotalMilliseconds;
                }
                else
                {
                    if (currpos <= _savedPausePosition)
                    {
                        Bar.Visibility = Visibility.Collapsed;
                        return;
                    }
                    x2 = len * currpos.TotalMilliseconds / MediaDuration.TotalMilliseconds;
                }
                x2 += pos.X;
                var x = pos.X + len * _savedPausePosition.TotalMilliseconds / MediaDuration.TotalMilliseconds;

                if (x2 <= x)
                {
                    Bar.Visibility = Visibility.Collapsed;
                    return;
                }

                Bar.SetValue(Canvas.LeftProperty, x);
                Bar.Width = x2 - x;

                Bar.SetValue(Canvas.TopProperty, pos.Y + 18);
                Bar.Height = height - 21;

                Bar.Visibility = Visibility.Visible;
            }
            else
            {
                Bar.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region UI operations

        /// <summary>
        /// Plays the media. Marshals call onto the UI thread's dispatcher.
        /// </summary>
        private void PlayMedia()
        {
            // If the Play button is pressed while the media is fast-forwarding or rewinding,
            // reset the PlaybackRate to the normal rate.
            StartBackgroundAudioPlayback();
        }

        /// <summary>
        /// Pauses the media. Marshals call onto the UI thread's dispatcher. 
        /// </summary>
        private void PauseMedia()
        {
            Pause();
            _lastPausePosition = Media.Position;
        }

        /// <summary>
        ///  Starts repeating so the stop repeating button is enabled
        /// </summary>
        private void StartRepeating()
        {
            _repeating = true;
            BtnStopRepeat.IsEnabled = true;
           // BtnBackRepeat.IsEnabled = false;
        }

        /// <summary>
        ///  Stops repeating so the stop repeating button is disabled
        /// </summary>
        private void StopRepeating()
        {
            _savedPausePosition = _endPosition;
            _repeating = false;
            BtnStopRepeat.IsEnabled = false;
        }

        #endregion


#if DEBUG
        /// <summary>
        ///  List for displaying debug messaage
        /// </summary>
        /// <param name="sender">The sender of teh event</param>
        /// <param name="args">The args of the event</param>
        private void MessagesOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    switch (args.Action)
                    {
                        case NotifyCollectionChangedAction.Add:
                            foreach (var item in args.NewItems)
                            {
                                // ReSharper disable once PossibleNullReferenceException
                                LstMessages.Items.Insert(0, item);
                            }
                            break;
                        default:
                            // ReSharper disable once PossibleNullReferenceException
                            LstMessages.Items.Clear();
                            foreach (var item in _debugger.Messages.Reverse())
                            {
                                LstMessages.Items.Add(item);
                            }
                            break;
                    }
                });
        }
#endif

        #endregion
    }
}
