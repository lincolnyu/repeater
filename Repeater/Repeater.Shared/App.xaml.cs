using Windows.UI.Core;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Globalization;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Repeater.Common;
#if WINDOWS_PHONE_APP
using Windows.Phone.UI.Input;
#endif

// The Blank Application template is documented at http://go.microsoft.com/fwlink/?LinkId=234227

namespace Repeater
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public sealed partial class App
    {
        #region Fields

#if WINDOWS_PHONE_APP
        private ContinuationManager _continuationManager;
#endif
        #endregion

        #region Events

#if WINDOWS_PHONE_APP
        /// <summary>
        /// This event wraps HardwareButtons.BackPressed to ensure that any pages that
        /// want to override the default behavior can subscribe to this event to potentially
        /// handle the back button press a different way (e.g. dismissing dialogs).
        /// </summary>
        public event EventHandler<BackPressedEventArgs> BackPressed;
#endif

        #endregion

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;

#if WINDOWS_PHONE_APP
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
#endif
        }

        private Frame CreateRootFrame()
        {
            var rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame
                {
                    // TODO: change this value to a cache size that is appropriate for your application
                    //   CacheSize = 1, // TODO NEED this?
                    // Set the default language
                    Language = ApplicationLanguages.Languages[0]
                };

                rootFrame.NavigationFailed += OnNavigationFailed;

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            return rootFrame;
        }

#if WINDOWS_PHONE_APP
        /// <summary>
        /// Handles the back button press and navigates through the history of the root frame.
        /// </summary>
        /// <param name="sender">The source of the event. <see cref="HardwareButtons"/></param>
        /// <param name="e">Details about the back button press.</param>
        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null)
            {
                return;
            }

            var handler = BackPressed;
            if (handler != null)
            {
                handler(sender, e);
            }

            if (frame.CanGoBack && !e.Handled)
            {
                frame.GoBack();
                e.Handled = true;
            }
        }
#endif

        private async Task RestoreStatusAsync(ApplicationExecutionState previousExecutionState)
        {
            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (previousExecutionState == ApplicationExecutionState.Terminated)
            {
                // Restore the saved session state only when appropriate
                try
                {
                    await SuspensionManager.RestoreAsync();
                }
                catch (SuspensionManagerException)
                {
                    //Something went wrong restoring state.
                    //Assume there is no state and continue
                }
            }
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used when the application is launched to open a specific file, to display
        /// search results, and so forth.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected async override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var rootFrame = CreateRootFrame();
            await RestoreStatusAsync(e.PreviousExecutionState);

            Window.Current.VisibilityChanged += CurrentWindowOnVisibilityChanged;
            
            //MainPage is always in rootFrame so we don't have to worry about restoring the navigation state on resume
            rootFrame.Navigate(typeof(MainPage), e.Arguments);

            // Ensure the current window is active
            Window.Current.Activate();
        }

        /// <summary>
        ///  When the visibility of the window of the app has changed (switched to start/task manager/other task/back out...)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void CurrentWindowOnVisibilityChanged(object sender, VisibilityChangedEventArgs args)
        {
            if (args.Visible)
            {
                // update the UI
                MainPage.Current.CheckBackground();
            }
            else
            {
                // the app window has been navigated from
            }
        }

#if WINDOWS_PHONE_APP
        protected async override void OnActivated(IActivatedEventArgs e)
        {
            base.OnActivated(e);

            _continuationManager = new ContinuationManager();

            var rootFrame = CreateRootFrame();
            await RestoreStatusAsync(e.PreviousExecutionState);

            if (rootFrame.Content == null)
            {
                rootFrame.Navigate(typeof(MainPage));
            }

            var continuationEventArgs = e as IContinuationActivatedEventArgs;
            if (continuationEventArgs != null)
            {
                // Call ContinuationManager to handle continuation activation
                _continuationManager.Continue(continuationEventArgs, MainPage.Current);
            }

            Window.Current.Activate();
        }
#endif

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();

            // TODO: Save application state and stop any background activity
            await SuspensionManager.SaveAsync();

            deferral.Complete();
        }
    }
}
