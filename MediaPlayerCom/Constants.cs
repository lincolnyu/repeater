namespace MediaPlayerCom
{
    /// <summary>
    /// Collection of string constants used in the entire solution. This file is shared for all projects
    /// </summary>
    public static class Constants
    {
        #region Background task states

        public const string CurrentTrack = "trackname";       
        public const string CurrentPosition = "currentposition";
        public const string PlayList = "playlist";
        
        #endregion

        #region Backgrounds state to notify the foreground of
        
        public const string BackgroundTaskState = "backgroundtaskstate"; // message type with subtypes below
        public const string BackgroundTaskStarted = "BackgroundTaskStarted";
        public const string BackgroundTaskRunning = "BackgroundTaskRunning";
        public const string BackgroundTaskCancelled = "BackgroundTaskCancelled";

        #endregion

        #region Foreground app status

        public const string AppState = "appstate";
        public const string ForegroundAppActive = "Active"; // these two should be identical to constants of ForegroundAppStatus
        public const string ForegroundAppSuspended = "Suspended";

        #endregion

        #region Commands/messages from foreground

        public const string Check = "check";
        public const string AppSuspended = "appsuspend";
        public const string AppResumed = "appresumed";
        public const string AddTrack = "addtrack";
        public const string ClearTracks = "cleartracks";
        public const string RepeatMode = "repeatmode";
        public const string OpenTrack = "opentrack";
        public const string StartPlayback = "startplayback";
        public const string Pause = "pause";
        public const string SkipNext = "skipnext";
        public const string SkipPrevious = "skipprevious";
        public const string Position = "position"; // saved position
        public const string CheckBackground = "checkbackground";

        #endregion

        #region Messages from background

        public const string TrackChanged = "songchanged";       // message sent to foreground
        public const string CurrentTrackIndex = "trackindex";   // message sent to foreground; also used for background status
        public const string TotalTracks = "tracknum";
        public const string UpdateUi = "updateui";
        public const string BackgroundCanceled = "backgroundcanceled";

        #endregion
    }
}
