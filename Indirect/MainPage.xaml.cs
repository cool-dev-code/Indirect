﻿using System;
using System.Linq;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Notifications;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using Indirect.Controls;
using Indirect.Wrapper;
using InstagramAPI;
using InstagramAPI.Classes.User;
using Microsoft.Toolkit.Uwp.UI.Controls;
using Microsoft.Toolkit.Uwp.UI.Extensions;
using CoreWindowActivationState = Windows.UI.Core.CoreWindowActivationState;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Indirect
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public static readonly DependencyProperty InboxProperty = DependencyProperty.Register(
            nameof(Inbox),
            typeof(InstaDirectInboxWrapper),
            typeof(MainPage),
            new PropertyMetadata(null));

        internal InstaDirectInboxWrapper Inbox
        {
            get => (InstaDirectInboxWrapper) GetValue(InboxProperty);
            set => SetValue(InboxProperty, value);
        }

        private ApiContainer ViewModel => ((App) Application.Current).ViewModel;
        private readonly Windows.Storage.ApplicationDataContainer _localSettings =
            Windows.Storage.ApplicationData.Current.LocalSettings;


        public MainPage()
        {
            this.InitializeComponent();
            Window.Current.SetTitleBar(TitleBarElement);
            MainLayout.ViewStateChanged += OnViewStateChange;
            Window.Current.Activated += OnWindowFocusChange;
            Window.Current.SizeChanged += OnWindowSizeChanged;
            Inbox = ViewModel.Inbox;
            MediaPopup.Width = Window.Current.Bounds.Width;
            MediaPopup.Height = Window.Current.Bounds.Height - 32;
        }

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            MediaPopup.Width = e.Size.Width;
            MediaPopup.Height = e.Size.Height - 32;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e?.NavigationMode != NavigationMode.Back)
            {
                await ViewModel.OnLoggedIn();
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            var confirmDialog = new ContentDialog()
            {
                Title = "Are you sure?",
                Content = "Logging out will delete all session data, including cached images and videos.",
                CloseButtonText = "Close",
                PrimaryButtonText = "Logout", 
                DefaultButton = ContentDialogButton.Close
            };
            var confirmation = await confirmDialog.ShowAsync();
            if (confirmation != ContentDialogResult.Primary) return;
            ViewModel.Logout();
            Frame.Navigate(typeof(Login));
        }
        
        private void DetailsBackButton_OnClick(object sender, RoutedEventArgs e) => ViewModel.SetSelectedThreadNull();

        private void OnWindowFocusChange(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == CoreWindowActivationState.Deactivated)
            {
                BackButton.IsEnabled = false;
                AppTitleTextBlock.Opacity = 0.5;
            }
            else
            {

                BackButton.IsEnabled = true;
                AppTitleTextBlock.Opacity = 1;
            }
        }

        private void OnViewStateChange(object sender, MasterDetailsViewState state)
        {
            BackButton.Visibility = state == MasterDetailsViewState.Details ? Visibility.Visible : Visibility.Collapsed;
            BackButtonPlaceholder.Visibility = BackButton.Visibility;
        }

        private void MainLayout_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0 || e.AddedItems[0] == null)
            {
                return;
            }
            var inboxThread = (InstaDirectInboxThreadWrapper) e.AddedItems[0];
            if (!string.IsNullOrEmpty(inboxThread.ThreadId)) 
                ToastNotificationManager.History.RemoveGroup(inboxThread.ThreadId);
            ViewModel.MarkLatestItemSeen(inboxThread);
            
            var details = (TextBox) MainLayout.FindDescendantByName("MessageTextBox");
            details?.Focus(FocusState.Programmatic);    // Focus to chat box after selecting a thread
        }

        private void SearchBox_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            if (string.IsNullOrEmpty(sender.Text) || sender.Text.Length > 50)
            {
                return;
            }

            ViewModel.Search(sender.Text,
                updatedList => SearchBox.ItemsSource = updatedList);
        }

        private void SearchBox_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var selectedItem = (InstaDirectInboxThreadWrapper) args.SelectedItem;
            sender.Text = selectedItem.Title;
        }

        private void SearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null)
            {
                ViewModel.MakeProperInboxThread((InstaDirectInboxThreadWrapper) args.ChosenSuggestion);
            }
            else if (!string.IsNullOrEmpty(sender.Text))
            {
                ViewModel.Search(sender.Text, updatedList =>
                {
                    if (updatedList.Count == 0) return;
                    ViewModel.MakeProperInboxThread(updatedList[0]);
                });
            }
            sender.Text = string.Empty;
            sender.ItemsSource = null;
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutDialog();
            _ = about.ShowAsync();
        }

        private void ThemeItem_Click(object sender, RoutedEventArgs e)
        {
            var item = (MenuFlyoutItem) sender;
            switch (item.Text)
            {
                case "System":
                    _localSettings.Values["Theme"] = "System";
                    break;

                case "Dark":
                    _localSettings.Values["Theme"] = "Dark";
                    break;
                
                case "Light":
                    _localSettings.Values["Theme"] = "Light";
                    break;
            }

            var dialog = new ContentDialog
            {
                Title = "Saved",
                Content = "Please relaunch the app to see the result.",
                CloseButtonText = "Done",
                DefaultButton = ContentDialogButton.Close
            };

            _ = dialog.ShowAsync();
        }

        private async void Profile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ViewModel?.LoggedInUser?.Username)) return;
            var username = ViewModel.LoggedInUser.Username;
            var uri = new Uri("https://www.instagram.com/" + username);
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }

        #region NewMessage

        private void NewMessageSuggestBox_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            if (string.IsNullOrEmpty(sender.Text) || sender.Text.Length > 50)
            {
                return;
            }

            ViewModel.SearchWithoutThreads(sender.Text,
                updatedList => NewMessageSuggestBox.ItemsSource = updatedList);
        }

        private void NewMessageSuggestBox_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var selectedItem = (BaseUser) args.SelectedItem;
            sender.Text = selectedItem.Username;
        }

        private void NewMessageSuggestBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null)
            {
                var selectedRecipient = (BaseUser) args.ChosenSuggestion;
                if (ViewModel.NewMessageCandidates.All(x => selectedRecipient.Username != x.Username))
                    ViewModel.NewMessageCandidates.Add(selectedRecipient);
            }
            else if (!string.IsNullOrEmpty(sender.Text))
            {
                ViewModel.SearchWithoutThreads(sender.Text, updatedList =>
                {
                    if (updatedList.Count == 0) return;
                    ViewModel.NewMessageCandidates.Add(updatedList[0]);
                });
            }
            sender.Text = string.Empty;
            sender.ItemsSource = null;
        }

        private void NewMessageClearAll_OnClick(object sender, RoutedEventArgs e)
        {
            ViewModel.NewMessageCandidates.Clear();
        }

        private async void ChatButton_OnClick(object sender, RoutedEventArgs e)
        {
            NewThreadFlyout.Hide();
            await ViewModel.CreateThread();
            ViewModel.NewMessageCandidates.Clear();
        }

        private void ClearSingleCandidateButton_OnClick(object sender, RoutedEventArgs e)
        {
            var target = (BaseUser) (sender as FrameworkElement)?.DataContext;
            if (target == null) return;
            ViewModel.NewMessageCandidates.Remove(target);
        }

        private void Candidate_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            (sender as FrameworkElement).FindDescendantByName("ClearSingleCandidateButton").Visibility = Visibility.Visible;
        }

        private void Candidate_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            (sender as FrameworkElement).FindDescendantByName("ClearSingleCandidateButton").Visibility = Visibility.Collapsed;
        }

        private void ClearSingleCandidateSwipe_OnInvoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
        {
            var target = (BaseUser) args.SwipeControl.DataContext;
            if (target == null) return;
            ViewModel.NewMessageCandidates.Remove(target);
        }

        private void NewMessageSuggestBox_OnProcessKeyboardAccelerators(UIElement sender, ProcessKeyboardAcceleratorEventArgs args)
        {
            if (args.Key == VirtualKey.Escape && args.Modifiers == VirtualKeyModifiers.None)
            {
                args.Handled = true;
                NewThreadFlyout.Hide();
            }
                
        }

        #endregion

        private void TogglePendingInbox_OnClick(object sender, RoutedEventArgs e)
        {
            Inbox = Inbox == ViewModel.Inbox ? ViewModel.PendingInbox : ViewModel.Inbox;
        }

        private void CloseMediaPopup_OnClick(object sender, RoutedEventArgs e)
        {
            CloseImmersiveView();
        }

        internal void OpenImmersiveView(object item)
        {
            MediaPopup.IsOpen = true;
            ImmersiveControl.Item = item;
            ImmersiveControl.Focus(FocusState.Programmatic);
        }

        internal void CloseImmersiveView()
        {
            MediaPopup.IsOpen = false;
            ImmersiveControl.OnClose();
        }

        private async void ReelsFeed_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var reelsFeed = (ListView) sender;
            int selected;
            lock (sender)
            {
                if (reelsFeed.SelectedIndex == -1) return;
                selected = reelsFeed.SelectedIndex;
                reelsFeed.SelectedIndex = -1;
            }
            var reelsWrapper = await ViewModel.ReelsFeed.PrepareFlatReelsContainer(selected);
            this.Frame.Navigate(typeof(ReelPage), reelsWrapper);
        }

        private async void StoriesSectionTitle_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            await ViewModel.ReelsFeed.UpdateReelsFeed(ReelsTrayFetchReason.PullToRefresh);
        }
    }
}
