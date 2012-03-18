using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Octgn.Data;
using Octgn.Definitions;
using Octgn.Play.Dialogs;
using Octgn.Play.Gui;
using Octgn.Scripting;
using Octgn.Utils;

namespace Octgn.Play
{
    public partial class PlayWindow
    {
        private bool _isLocal;
#pragma warning disable 649   // Unassigned variable: it's initialized by MEF

        [Import] protected Engine ScriptEngine;


#pragma warning restore 649

        #region Dependency Properties

        public bool IsFullScreen
        {
            get { return (bool) GetValue(IsFullScreenProperty); }
            set { SetValue(IsFullScreenProperty, value); }
        }

        public static readonly DependencyProperty IsFullScreenProperty =
            DependencyProperty.Register("IsFullScreen", typeof (bool), typeof (PlayWindow),
                                        new UIPropertyMetadata(false));

        #endregion

        public PlayWindow(bool islocal = false)
        {
            InitializeComponent();
            _isLocal = islocal;
            //Application.Current.MainWindow = this;
            Version oversion = Assembly.GetExecutingAssembly().GetName().Version;
            Title = "Octgn  version : " + oversion + " : " + Program.Game.Definition.Name;
            Program.Game.ComposeParts(this);
        }

        private Storyboard _fadeIn, _fadeOut;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Program.Dispatcher = Dispatcher;
            DataContext = Program.Game;

            _fadeIn = (Storyboard) Resources["ImageFadeIn"];
            _fadeOut = (Storyboard) Resources["ImageFadeOut"];

            cardViewer.Source = new BitmapImage(new Uri(Program.Game.Definition.CardDefinition.Back));
            if (Program.Game.Definition.CardDefinition.CornerRadius > 0)
                cardViewer.Clip = new RectangleGeometry();
            AddHandler(CardControl.CardHoveredEvent, new CardEventHandler(CardHovered));
            AddHandler(CardRun.ViewCardModelEvent, new EventHandler<CardModelEventArgs>(ViewCardModel));

            Loaded += (sender, args) => Keyboard.Focus(table);
            // Solve various issues, like disabled menus or non-available keyboard shortcuts

#if(!DEBUG)
            // Show the Scripting console in dev only
            if (Application.Current.Properties["ArbitraryArgName"] == null) return;
            string fname = Application.Current.Properties["ArbitraryArgName"].ToString();
            if (fname != "/developer") return;
#endif
            Console.Visibility = Visibility.Visible;
            Loaded += (sender, args) =>
                          {
                              var wnd = new InteractiveConsole {Owner = this};
                              wnd.Show();
                          };
        }

        private void InitializePlayerSummary(object sender, EventArgs e)
        {
            var textBlock = (TextBlock) sender;
            var player = textBlock.DataContext as Player;
            if (player != null && player.IsGlobalPlayer)
            {
                textBlock.Visibility = Visibility.Collapsed;
                return;
            }

            PlayerDef def = Program.Game.Definition.PlayerDefinition;
            string format = def.IndicatorsFormat;
            if (format == null)
            {
                textBlock.Visibility = Visibility.Collapsed;
                return;
            }

            var multi = new MultiBinding();
            int placeholder = 0;
            format = Regex.Replace(format, @"{#([^}]*)}", delegate(Match match)
                                                              {
                                                                  string name = match.Groups[1].Value;
                                                                  if (player != null)
                                                                  {
                                                                      Counter counter =
                                                                          player.Counters.FirstOrDefault(
                                                                              c => c.Name == name);
                                                                      if (counter != null)
                                                                      {
                                                                          multi.Bindings.Add(new Binding("Value")
                                                                                                 {Source = counter});
                                                                          return "{" + placeholder++ + "}";
                                                                      }
                                                                  }
                                                                  if (player != null)
                                                                  {
                                                                      Group group =
                                                                          player.IndexedGroups.FirstOrDefault(
                                                                              g => g.Name == name);
                                                                      if (@group != null)
                                                                      {
                                                                          multi.Bindings.Add(new Binding("Count")
                                                                                                 {Source = @group.Cards});
                                                                          return "{" + placeholder++ + "}";
                                                                      }
                                                                  }
                                                                  return "?";
                                                              });
            multi.StringFormat = format;
            textBlock.SetBinding(TextBlock.TextProperty, multi);
        }

        protected void Close(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (MessageBox.Show(
                "Are you sure you want to quit?",
                "Octgn",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
                e.Cancel = true;
            // Fix for this bug: http://wpf.codeplex.com/workitem/14078
            ribbon.IsMinimized = false;

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Program.PlayWindow = null;
            Program.StopGame();            
            //if(_isLocal)
            //    Program.LauncherWindow.Visibility = Visibility.Visible;
            // Fix: Don't do this earlier (e.g. in OnClosing) because an animation (e.g. card turn) may try to access Program.Game
        }

        private void Open(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            // Show the dialog to choose the file
            var ofd = new OpenFileDialog
                          {
                              Filter = "Octgn deck files (*.o8d) | *.o8d",
                              InitialDirectory = SimpleConfig.ReadValue("lastFolder")
                          };
            //ofd.InitialDirectory = Program.Game.Definition.DecksPath;
            if (ofd.ShowDialog() != true) return;
            SimpleConfig.WriteValue("lastFolder", Path.GetDirectoryName(ofd.FileName));
            // Try to load the file contents
            try
            {
                Deck newDeck = Deck.Load(ofd.FileName,
                                         Program.GamesRepository.Games.First(g => g.Id == Program.Game.Definition.Id));
                // Load the deck into the game
                Program.Game.LoadDeck(newDeck);
            }
            catch (DeckException ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Octgn couldn't load the deck.\r\nDetails:\r\n\r\n" + ex.Message, "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LimitedGame(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (LimitedDialog.Singleton == null)
                new LimitedDialog {Owner = this}.Show();
            else
                LimitedDialog.Singleton.Activate();
        }

        private void ToggleFullScreen(object sender, RoutedEventArgs e)
        {
            if (IsFullScreen)
            {
                Topmost = false;
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                menuRow.Height = GridLength.Auto;
            }
            else
            {
                Topmost = true;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                menuRow.Height = new GridLength(2);
            }
            IsFullScreen = !IsFullScreen;
        }

        private void ResetGame(object sender, RoutedEventArgs e)
        {
            // Prompt for a confirmation
            if (MessageBoxResult.Yes ==
                MessageBox.Show("The current game will end. Are you sure you want to continue?",
                                "Confirmation", MessageBoxButton.YesNo))
            {
                Program.Client.Rpc.ResetReq();
            }
        }

        protected void MouseEnteredMenu(object sender, RoutedEventArgs e)
        {
            if (!IsFullScreen) return;
            menuRow.Height = GridLength.Auto;
        }

        protected void MouseLeftMenu(object sender, RoutedEventArgs e)
        {
            if (!IsFullScreen) return;
            menuRow.Height = new GridLength(2);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.OriginalSource is TextBox)
                return; // Do not tinker with the keyboard events when the focus is inside a textbox
            if (e.IsRepeat)
                return;
            IInputElement mouseOver = Mouse.DirectlyOver;
            var te = new TableKeyEventArgs(this, e);
            if (mouseOver != null) mouseOver.RaiseEvent(te);
            if (te.Handled) return;

            // If the event was unhandled, check if there's a selection and try to apply a shortcut action to it
            if (!Selection.IsEmpty() && Selection.Source.CanManipulate())
            {
                ActionShortcut match =
                    Selection.Source.CardShortcuts.FirstOrDefault(
                        shortcut => shortcut.Key.Matches(this, te.KeyEventArgs));
                if (match != null)
                {
                    if (match.ActionDef.Execute != null)
                        ScriptEngine.ExecuteOnCards(match.ActionDef.Execute, Selection.Cards);
                    else if (match.ActionDef.BatchExecute != null)
                        ScriptEngine.ExecuteOnBatch(match.ActionDef.BatchExecute, Selection.Cards);
                    e.Handled = true;
                    return;
                }
            }

            // The event was still unhandled, try all groups, starting with the table
            table.RaiseEvent(te);
            if (te.Handled) return;
            foreach (Group g in Player.LocalPlayer.Groups.Where(g => g.CanManipulate()))
            {
                ActionShortcut a = g.GroupShortcuts.FirstOrDefault(shortcut => shortcut.Key.Matches(this, e));
                if (a == null) continue;
                if (a.ActionDef.Execute != null)
                    ScriptEngine.ExecuteOnGroup(a.ActionDef.Execute, g);
                e.Handled = true;
                return;
            }
        }

        private void CardHovered(object sender, CardEventArgs e)
        {
            if (e.Card == null && e.CardModel == null)
            {
                _fadeOut.Begin(outerCardViewer, HandoffBehavior.SnapshotAndReplace);
            }
            else
            {
                Point mousePt = Mouse.GetPosition(table);
                if (mousePt.X < 0.4*clientArea.ActualWidth)
                    outerCardViewer.HorizontalAlignment = cardViewer.HorizontalAlignment = HorizontalAlignment.Right;
                else if (mousePt.X > 0.6*clientArea.ActualWidth)
                    outerCardViewer.HorizontalAlignment = cardViewer.HorizontalAlignment = HorizontalAlignment.Left;

                var ctrl = e.OriginalSource as CardControl;
                BitmapImage img = e.Card != null
                                      ? e.Card.GetBitmapImage(ctrl != null && ctrl.IsAlwaysUp || (e.Card.FaceUp ||
                                                                                                  e.Card.PeekingPlayers.
                                                                                                      Contains(
                                                                                                          Player.
                                                                                                              LocalPlayer)))
                                      : ImageUtils.CreateFrozenBitmap(new Uri(e.CardModel.Picture));
                ShowCardPicture(img);
            }
        }

        /// <summary>
        /// This could possibly be the function used when enlarging a card to make selecting it easier,
        /// such as upon mouseover of a card that is but one of many in the hand.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ViewCardModel(object sender, CardModelEventArgs e)
        {
            if (e.CardModel == null)
                _fadeOut.Begin(outerCardViewer, HandoffBehavior.SnapshotAndReplace);
            else
                ShowCardPicture(ImageUtils.CreateFrozenBitmap(new Uri(e.CardModel.Picture)));
        }

        /// <summary>
        /// I suspect this is the function that gets called to show the very large image upon mouseover.
        /// With SPC, the image covers nearly half the table
        /// </summary>
        /// <param name="img"></param>
        private void ShowCardPicture(BitmapSource img)
        {
            cardViewer.Height = img.PixelHeight;
            cardViewer.Width = img.PixelWidth;
            cardViewer.Source = img;

            _fadeIn.Begin(outerCardViewer, HandoffBehavior.SnapshotAndReplace);

            if (cardViewer.Clip == null) return;
            CardDef cardDef = Program.Game.Definition.CardDefinition;
            var clipRect = ((RectangleGeometry) cardViewer.Clip);
            double height = Math.Min(cardViewer.MaxHeight, cardViewer.Height);
            double width = cardViewer.Width*height/cardViewer.Height;
            clipRect.Rect = new Rect(new Size(width, height));
            clipRect.RadiusX = clipRect.RadiusY = cardDef.CornerRadius*height/cardDef.Height;
        }

        private void NextTurnClicked(object sender, RoutedEventArgs e)
        {
            var btn = (ToggleButton) sender;
            var targetPlayer = (Player) btn.DataContext;
            if (Program.Game.TurnPlayer == null || Program.Game.TurnPlayer == Player.LocalPlayer)
                Program.Client.Rpc.NextTurn(targetPlayer);
            else
            {
                Program.Client.Rpc.StopTurnReq(Program.Game.TurnNumber, btn.IsChecked != null && btn.IsChecked.Value);
                if (btn.IsChecked != null) Program.Game.StopTurn = btn.IsChecked.Value;
            }
        }

        private void ActivateChat(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = true;
            chat.FocusInput();
        }

        private void ShowAboutWindow(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            //var wnd = new AboutWindow() { Owner = this };
            //wnd.ShowDialog();
            Process.Start("http://www.octgn.info");
        }

        private void ConsoleClicked(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var wnd = new InteractiveConsole {Owner = this};
            wnd.Show();
        }

        internal void ShowBackstage(UIElement ui)
        {
            table.Visibility = Visibility.Collapsed;
            wndManager.Visibility = Visibility.Collapsed;
            backstage.Child = ui;
            backstage.Visibility = Visibility.Visible;
            if (!(ui is PickCardsDialog)) return;
            limitedTab.Visibility = Visibility.Visible;
            ribbon.SelectedItem = limitedTab;
        }

        private void ShowRules(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var wnd = new RulesWindow {Owner = this};
            wnd.ShowDialog();
        }

        internal void HideBackstage()
        {
            limitedTab.Visibility = Visibility.Collapsed;

            table.Visibility = Visibility.Visible;
            wndManager.Visibility = Visibility.Visible;
            backstage.Visibility = Visibility.Collapsed;
            backstage.Child = null;

            Keyboard.Focus(table); // Solve various issues, like disabled menus or non-available keyboard shortcuts
        }

        #region Limited

        protected void LimitedSaveClicked(object sender, EventArgs e)
        {
            var sfd = new SaveFileDialog
                          {
                              AddExtension = true,
                              Filter = "Octgn decks|*.o8d",
                              InitialDirectory = Program.Game.Definition.DecksPath
                          };
            if (!sfd.ShowDialog().GetValueOrDefault()) return;

            var dlg = backstage.Child as PickCardsDialog;
            try
            {
                if (dlg != null) dlg.LimitedDeck.Save(sfd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occured while trying to save the deck:\n" + ex.Message, "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected void LimitedOkClicked(object sender, EventArgs e)
        {
            var dlg = backstage.Child as PickCardsDialog;
            if (dlg != null) Program.Game.LoadDeck(dlg.LimitedDeck);
            HideBackstage();
        }

        protected void LimitedCancelClicked(object sender, EventArgs e)
        {
            Program.Client.Rpc.CancelLimitedReq();
            HideBackstage();
        }

        #endregion
    }

    internal class CanPlayConverter : IMultiValueConverter
    {
        #region IMultiValueConverter Members

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var turnPlayer = values[0] as Player;
            var player = values[1] as Player;

            string styleKey;
            if (player == Player.GlobalPlayer)
                styleKey = "InvisibleButton";
            else if (turnPlayer == null)
                styleKey = "PlayButton";
            else if (turnPlayer == Player.LocalPlayer)
                styleKey = "PlayButton";
            else
                styleKey = turnPlayer == player ? "PauseButton" : "InvisibleButton";
            return Application.Current.FindResource(styleKey);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    internal class ScaleConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            var d = (double) value;
            double scale = double.Parse((string) parameter, CultureInfo.InvariantCulture);
            return d*scale;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}