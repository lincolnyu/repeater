using System;
using Windows.ApplicationModel.Activation;

namespace Repeater
{
#if WINDOWS_PHONE_APP
    /// <summary>
    /// ContinuationManager is used to detect if the most recent activation was due
    /// to a continuation such as the FileOpenPicker or WebAuthenticationBroker
    /// </summary>
    public class ContinuationManager
    {
        IContinuationActivatedEventArgs _args;
        bool _handled;
        Guid _id = Guid.Empty;

        /// <summary>
        /// Sets the ContinuationArgs for this instance. Should be called by the main activation
        /// handling code in App.xaml.cs
        /// </summary>
        /// <param name="args">The activation args</param>
        /// <param name="target">The target to get continued</param>
        internal void Continue(IContinuationActivatedEventArgs args, object target)
        {
            if (args == null)
            {
                throw new ArgumentNullException("args");
            }

            if (_args != null && !_handled)
            {
                throw new InvalidOperationException("Can't set args more than once");
            }

            _args = args;
            _handled = false;
            _id = Guid.NewGuid();

            if (target == null)
            {
                return;
            }

            switch (args.Kind)
            {
                case ActivationKind.PickFileContinuation:
                    var fileOpenPickerPage = target as IFileOpenPickerContinuable;
                    if (fileOpenPickerPage != null)
                    {
                        fileOpenPickerPage.ContinueFileOpenPicker(args as FileOpenPickerContinuationEventArgs);
                    }
                    break;

                case ActivationKind.PickSaveFileContinuation:
                    var fileSavePickerPage = target as IFileSavePickerContinuable;
                    if (fileSavePickerPage != null)
                    {
                        fileSavePickerPage.ContinueFileSavePicker(args as FileSavePickerContinuationEventArgs);
                    }
                    break;

                case ActivationKind.PickFolderContinuation:
                    var folderPickerPage = target as IFolderPickerContinuable;
                    if (folderPickerPage != null)
                    {
                        folderPickerPage.ContinueFolderPicker(args as FolderPickerContinuationEventArgs);
                    }
                    break;

                case ActivationKind.WebAuthenticationBrokerContinuation:
                    var wabPage = target as IWebAuthenticationContinuable;
                    if (wabPage != null)
                    {
                        wabPage.ContinueWebAuthentication(args as WebAuthenticationBrokerContinuationEventArgs);
                    }
                    break;
            }
        }

        /// <summary>
        /// Marks the contination data as 'stale', meaning that it is probably no longer of
        /// any use. Called when the app is suspended (to ensure future activations don't appear
        /// to be for the same continuation) and whenever the continuation data is retrieved 
        /// (so that it isn't retrieved on subsequent navigations)
        /// </summary>
        internal void MarkAsStale()
        {
            _handled = true;
        }

        /// <summary>
        /// Retrieves the continuation args, if they have not already been retrieved, and 
        /// prevents further retrieval via this property (to avoid accidentla double-usage)
        /// </summary>
        public IContinuationActivatedEventArgs ContinuationArgs
        {
            get
            {
                if (_handled)
                    return null;
                MarkAsStale();
                return _args;
            }
        }

        /// <summary>
        /// Unique identifier for this particular continuation. Most useful for components that 
        /// retrieve the continuation data via <see cref="GetContinuationArgs"/> and need
        /// to perform their own replay check
        /// </summary>
        public Guid Id { get { return _id; } }

        /// <summary>
        /// Retrieves the continuation args, optionally retrieving them even if they have already
        /// been retrieved
        /// </summary>
        /// <param name="includeStaleArgs">Set to true to return args even if they have previously been returned</param>
        /// <returns>The continuation args, or null if there aren't any</returns>
        public IContinuationActivatedEventArgs GetContinuationArgs(bool includeStaleArgs)
        {
            if (!includeStaleArgs && _handled)
                return null;
            MarkAsStale();
            return _args;
        }
    }

#endif
}