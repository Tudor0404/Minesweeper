using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Minesweeper {

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    ///
    public struct DialogTemplate {
        private string title;
        private string content;

        public string Title { get => title; set => title = value; }
        public string Content { get => content; set => content = value; }
    }

    public partial class MainWindow : Window {
        private int gridWidth = 9;
        private int gridHeight = 9;
        private int bombCount = 10;

        private bool gameInProgress = false;
        private bool gameFinished = false;
        private int gameIndex;

        private int currentSeed;
        private Random rnd = new Random();
        private bool seedWasRandom;

        private int[,] boardArray; // 0 -> 8 how many bombs around, 10 = bomb
        private int[,] boardDiscovered; // 0 = not discovered, 1 = discovered, 2 = flag

        private DispatcherTimer dispatcherTimer = new DispatcherTimer();
        private DateTime startTime;
        private TimeSpan runTime;

        public static List<(string, int, int, int)> difficulties = new List<(string, int, int, int)>() {
                ("Easy", 9, 9, 10),
                ("Medium", 16, 16, 40),
                ("Hard", 30, 16, 99),
                ("Extreme", 60, 32, 400),
                ("Impossible", 100, 60, 1000),
            }; // name, width, height, bomb count

        public MainWindow() {
            PastRuns.LoadRuns();

            InitializeComponent();

            ComboBoxSelectDifficulty.ItemsSource = difficulties.Select(c => $"{c.Item1} - {c.Item4} bombs");
            ComboBoxSelectDifficulty.SelectedIndex = 0;

            foreach (var item in difficulties) {
                ListBoxItem tab = new ListBoxItem();
                tab.Content = item.Item1;
                tab.Padding = new Thickness(2);
                RadioButtonGroupChoiceChip.Items.Add(tab);
            }

            RadioButtonGroupChoiceChip.SelectedIndex = 0;

            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 1);
        }

        #region transforming play area

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) {
            SetGridLengths();
        }

        private const double scaleRate = 1.1;
        private ScaleTransform scaleTransform = new ScaleTransform();
        private bool added;

        private Point lastPoint;
        private Point translateOffset;

        private bool isDragged;

        private void GridPlayArea_MouseWheel(object sender, MouseWheelEventArgs e) {
            if (e.Delta > 0) {
                scaleTransform.ScaleX *= scaleRate;
                scaleTransform.ScaleY *= scaleRate;
            } else {
                scaleTransform.ScaleX /= scaleRate;
                scaleTransform.ScaleY /= scaleRate;
            }

            if (scaleTransform.ScaleX < 1) {
                scaleTransform.ScaleX = 1;
                scaleTransform.ScaleY = 1;
            }

            if (!added) {
                TransformGroup tg = GridPlayArea.RenderTransform as TransformGroup;
                if (tg != null) {
                    tg.Children.Add(scaleTransform);
                    GridPlayArea.RenderTransformOrigin = new Point(0.5, 0.5);
                    added = true;
                }
            }
        }

        private void GridPlayArea_MouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Middle) {
                GridPlayArea.CaptureMouse();
                lastPoint = e.GetPosition(CardPlayAreaContainer);
                isDragged = true;
            }
        }

        private void GridPlayArea_MouseUp(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Middle) {
                GridPlayArea.ReleaseMouseCapture();
                isDragged = false;
            }
        }

        /// <summary>
        /// Reset grid transform to bring it into view
        /// </summary>
        private void ResetTransforms() {
            scaleTransform.ScaleX = 1;
            scaleTransform.ScaleY = 1;

            var matrix = GridPlayAreaMatrixTransform.Matrix;
            matrix.Translate(-translateOffset.X, -translateOffset.Y);
            GridPlayAreaMatrixTransform.Matrix = matrix;

            translateOffset = new Point();
        }

        /// <summary>
        /// On mouse move event to enable panning, the event is on the container and not on the grid to prevent jittering (idk why that happens)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CardPlayAreaContainer_MouseMove(object sender, MouseEventArgs e) {
            if (isDragged == false) {
                return;
            }

            var pos = e.GetPosition(CardPlayAreaContainer);
            if (GridPlayArea.IsMouseCaptured && pos != lastPoint) {
                var matrix = GridPlayAreaMatrixTransform.Matrix;
                double difX = (pos.X - lastPoint.X) / scaleTransform.ScaleX;
                double difY = (pos.Y - lastPoint.Y) / scaleTransform.ScaleY;
                matrix.Translate(difX, difY);
                GridPlayAreaMatrixTransform.Matrix = matrix;
                translateOffset.X += difX;
                translateOffset.Y += difY;
                lastPoint = pos;
            }
        }

        #endregion transforming play area

        #region grid cell handlers

        private string lastName;

        private void GridCell_MouseLeftButtonDown(object sender, EventArgs e) {
            lastName = ((Rectangle)sender).Name;
        }

        private void GridCell_MouseLeftButtonUp(object sender, EventArgs e) {
            string name = ((Rectangle)sender).Name;
            if (name == lastName && !gameFinished) {
                string[] nameData = name.Split('_');
                if (!gameInProgress) {
                    CreateBoardArray(Int32.Parse(nameData[1]), Int32.Parse(nameData[2]));
                    dispatcherTimer.Start();
                } else {
                    DiscoverCell(Int32.Parse(nameData[1]), Int32.Parse(nameData[2]));
                }
            }
            lastName = "";
        }

        private void GridCell_MouseRightButtonDown(object sender, EventArgs e) {
            lastName = ((Rectangle)sender).Name;
        }

        private void GridCell_MouseRightButtonUp(object sender, EventArgs e) {
            string name = ((Rectangle)sender).Name;
            if (name == lastName && !gameFinished) {
                string[] nameData = name.Split('_');
                if (gameInProgress) {
                    SetFlag(Int32.Parse(nameData[1]), Int32.Parse(nameData[2]));
                }
            }
            lastName = "";
        }

        private void ButtonResetTransform_Click(object sender, RoutedEventArgs e) {
            ResetTransforms();
        }

        #endregion grid cell handlers

        #region general UI handlers

        private void RadioButtonGroupChoiceChip_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            SetPastRunsListBox();
        }

        private void SetPastRunsListBox() {
            ListBoxPastRuns.Items.Clear();

            List<string> temp = new List<string>();
            int selectedIndex = RadioButtonGroupChoiceChip.SelectedIndex;
            var runs = PastRuns.GetByIndex(selectedIndex);

            if (runs.Count == 0) {
                temp.Add("No past runs recorded");
            } else {
                temp = runs.OrderBy(c => c.Time).Select(c => $"{String.Format("{0:00}:{1:00}:{2:00}", c.Time.Minutes, c.Time.Seconds, c.Time.Milliseconds)} on {c.DateStarted.ToShortDateString()}").ToList();
            }

            int count = 0;

            temp.ForEach(c => {
                ListBoxItem item = new ListBoxItem();
                item.Content = c;
                item.Name = $"_{count}";
                count++;
                ListBoxPastRuns.Items.Add(c);
            }
            );
        }

        private void ListBoxContainerPastRuns_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            ScrollViewer scv = (ScrollViewer)sender;
            scv.ScrollToVerticalOffset(scv.VerticalOffset - (e.Delta * 0.5));
            e.Handled = true;
        }

        private void ScrollViewerSettings_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            ScrollViewer scv = (ScrollViewer)sender;
            scv.ScrollToVerticalOffset(scv.VerticalOffset - (e.Delta * 0.5));
            e.Handled = true;
        }

        private void ButtonStartGame_Click(object sender, RoutedEventArgs e) {
            if (TextBoxSeed.Text != "") {
                currentSeed = TextBoxSeed.Text.GetHashCode();
                seedWasRandom = false;
            } else {
                currentSeed = (int)DateTime.Now.Ticks;
                seedWasRandom = true;
            }
            rnd = new Random(currentSeed);

            SetGrid();
            ResetTransforms();
            ResetTimer();

            TextBoxSeed.Text = "";
        }

        private void DialogClosing_DeleteRun(object sender, MaterialDesignThemes.Wpf.DialogClosingEventArgs e) {
            if (e.Parameter != null) {
                try {
                    PastRuns.runs[difficulties[RadioButtonGroupChoiceChip.SelectedIndex].Item1].Remove(PastRuns.runs[difficulties[RadioButtonGroupChoiceChip.SelectedIndex].Item1].First(c => c.DateStarted == (DateTime)e.Parameter));
                    PastRuns.SaveRuns();
                    SetPastRunsListBox();
                } catch { }
            }
        }

        /// <summary>
        /// https://stackoverflow.com/questions/21993558/how-to-get-index-of-a-listbox-item-without-selectedindex-or-the-like-in-preview
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        private void ListBoxPastRuns_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            // you can check which mouse button, its state, or use the correct event.

            // get the element the mouse is currently over
            var uie = ListBoxPastRuns.InputHitTest(Mouse.GetPosition(ListBoxPastRuns));

            if (uie == null) {
                return;
            }

            // navigate to its ListBoxItem container
            var listBoxItem = FindParent<ListBoxItem>((FrameworkElement)uie);

            // in case the click was not over a listBoxItem
            if (listBoxItem == null) {
                return;
            }

            // here is the index
            int index = ListBoxPastRuns.ItemContainerGenerator.IndexFromContainer(listBoxItem);
            if (PastRuns.GetByIndex(RadioButtonGroupChoiceChip.SelectedIndex).Count != 0) {
                MaterialDesignThemes.Wpf.DialogHost.Show(PastRuns.runs[difficulties[RadioButtonGroupChoiceChip.SelectedIndex].Item1][index], "RootDialog");
            }
        }

        public static T FindParent<T>(FrameworkElement child) where T : DependencyObject {
            T parent = null;
            var currentParent = VisualTreeHelper.GetParent(child);

            while (currentParent != null) {
                // check the current parent
                if (currentParent is T) {
                    parent = (T)currentParent;
                    break;
                }

                // find the next parent
                currentParent = VisualTreeHelper.GetParent(currentParent);
            }

            return parent;
        }

        #endregion general UI handlers

        #region game logic

        /// <summary>
        /// Resets the clock and clock text
        /// </summary>
        private void ResetTimer() {
            dispatcherTimer.Stop();
            startTime = DateTime.Now;
            TextClock.Text = "00:00:00";
        }

        /// <summary>
        /// Updates the clock
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dispatcherTimer_Tick(object sender, EventArgs e) {
            runTime = DateTime.Now - startTime;
            TextClock.Text = String.Format("{0:00}:{1:00}:{2:00}", runTime.Minutes, runTime.Seconds, runTime.Milliseconds);
        }

        /// <summary>
        /// Generate the grid in UI, adds event handlers to the UIElements
        /// </summary>
        private void SetGrid() {
            gameInProgress = false;
            gameFinished = false;
            GridPlayArea.Children.Clear();
            gameIndex = ComboBoxSelectDifficulty.SelectedIndex;
            gridWidth = difficulties[gameIndex].Item2;
            gridHeight = difficulties[gameIndex].Item3;
            bombCount = difficulties[gameIndex].Item4;

            SetPastRunsListBox();

            double cardHeight = CardPlayAreaContainer.ActualHeight - 8;
            double cardWidth = CardPlayAreaContainer.ActualWidth - 8;

            double length = cardWidth / gridWidth;
            length = length * gridHeight > cardHeight ? cardHeight / gridHeight : length;

            boardArray = new int[gridWidth, gridHeight];
            boardDiscovered = new int[gridWidth, gridHeight];

            SetBombText();

            GridPlayArea.ColumnDefinitions.Clear();
            for (int i = 0; i < gridWidth; i++) {
                ColumnDefinition tempCD = new ColumnDefinition();
                tempCD.Width = new GridLength(length, GridUnitType.Pixel);
                GridPlayArea.ColumnDefinitions.Add(tempCD);
            }

            GridPlayArea.RowDefinitions.Clear();
            for (int i = 0; i < gridHeight; i++) {
                RowDefinition tempRD = new RowDefinition();
                tempRD.Height = new GridLength(length, GridUnitType.Pixel);
                GridPlayArea.RowDefinitions.Add(tempRD);
            }

            for (int c = 0; c < gridWidth; c++) {
                for (int r = 0; r < gridHeight; r++) {
                    Rectangle rect = new Rectangle();

                    boardArray[c, r] = 0;
                    boardDiscovered[c, r] = 0;

                    rect.Fill = new ImageBrush() {
                        ImageSource = new BitmapImage(new Uri(@"assets\unknown.png", UriKind.Relative))
                    };

                    rect.Height = length;
                    rect.Width = length;
                    rect.Name = $"_{c}_{r}";
                    rect.MouseLeftButtonDown += GridCell_MouseLeftButtonDown;
                    rect.MouseLeftButtonUp += GridCell_MouseLeftButtonUp;
                    rect.MouseRightButtonDown += GridCell_MouseRightButtonDown;
                    rect.MouseRightButtonUp += GridCell_MouseRightButtonUp;
                    GridPlayArea.Children.Add(rect);
                    Grid.SetRow(rect, r);
                    Grid.SetColumn(rect, c);
                }
            }

            ResetTimer();
        }

        /// <summary>
        /// Updates grid dimensions based on available space
        /// </summary>
        private void SetGridLengths() {
            double cardHeight = CardPlayAreaContainer.ActualHeight - 8;
            double cardWidth = CardPlayAreaContainer.ActualWidth - 8;

            double length = cardWidth / gridWidth;
            length = length * gridHeight > cardHeight ? cardHeight / gridHeight : length;

            foreach (var item in GridPlayArea.Children.OfType<Rectangle>()) {
                item.Height = length;
                item.Width = length;
            }

            GridLength tempGridLength = new GridLength(length, GridUnitType.Pixel);

            foreach (var item in GridPlayArea.ColumnDefinitions) {
                item.Width = tempGridLength;
            }

            foreach (var item in GridPlayArea.RowDefinitions) {
                item.Height = tempGridLength;
            }
        }

        /// <summary>
        /// Creates a int array of the board
        /// </summary>
        /// <param name="startCol">column index exlude zone</param>
        /// <param name="startRow">row index exclude zone</param>
        private void CreateBoardArray(int startCol, int startRow) {
            List<(int, int)> indexesInGrid = new List<(int, int)>();
            for (int c = 0; c < gridWidth; c++) {
                for (int r = 0; r < gridHeight; r++) {
                    if ((c != startCol && c != startCol - 1 && c != startCol + 1) || (r != startRow && r != startRow - 1 && r != startRow + 1)) {
                        indexesInGrid.Add((c, r));
                    }
                }
            }

            for (int i = 0; i < bombCount; i++) {
                int indexSelected = rnd.Next(indexesInGrid.Count);
                boardArray[indexesInGrid[indexSelected].Item1, indexesInGrid[indexSelected].Item2] = 10;
                indexesInGrid.RemoveAt(indexSelected);
            }

            for (int c = 0; c < gridWidth; c++) {
                for (int r = 0; r < gridHeight; r++) {
                    if (boardArray[c, r] == 10) {
                        IEnumerable<(int, int)> nearbyIndexes = new List<(int, int)>() {
                        (c-1, r-1), (c, r-1), (c+1, r-1),
                        (c-1, r),             (c+1, r),
                        (c-1, r+1), (c, r+1), (c+1, r+1)
                     }.Where(c => c.Item1 >= 0 && c.Item1 <= gridWidth - 1 && c.Item2 >= 0 && c.Item2 <= gridHeight - 1);
                        foreach (var item in from item in nearbyIndexes
                                             where boardArray[item.Item1, item.Item2] != 10
                                             select item) {
                            boardArray[item.Item1, item.Item2] += 1;
                        }
                    }
                }
            }

            gameInProgress = true;
            DiscoverCell(startCol, startRow);
        }

        /// <summary>
        /// Shows all the board, and sets image to a red tint if the cell hasn't been discovered (or if flag not on bomb)
        /// </summary>
        private void ShowAllBoard() {
            for (int c = 0; c < gridWidth; c++) {
                for (int r = 0; r < gridHeight; r++) {
                    SetCellImage(c, r, (boardDiscovered[c, r] == 2 && boardArray[c, r] == 10) ? false : (boardDiscovered[c, r] != 1));
                }
            }
        }

        /// <summary>
        /// Discovers cells around point, also checks if lost
        /// </summary>
        /// <param name="c">column index</param>
        /// <param name="r">row index</param>
        private void DiscoverCell(int c, int r) {
            SetCellImage(c, r);

            if (boardDiscovered[c, r] == 2) {
                SetFlag(c, r);
                return;
            }

            boardDiscovered[c, r] = 1;
            if (boardArray[c, r] == 0) {
                foreach (var item in from coord in new List<(int, int)>() {
                        (c-1, r-1), (c, r-1), (c+1, r-1),
                        (c-1, r),             (c+1, r),
                        (c-1, r+1), (c, r+1), (c+1, r+1)
                     }.Where(c => c.Item1 >= 0 && c.Item1 <= gridWidth - 1 && c.Item2 >= 0 && c.Item2 <= gridHeight - 1)
                                     where boardDiscovered[coord.Item1, coord.Item2] == 0
                                     select coord) {
                    DiscoverCell(item.Item1, item.Item2);
                }
            } else if (boardArray[c, r] == 10) {
                boardDiscovered[c, r] = 0;
                ShowAllBoard();
                dispatcherTimer.Stop();
                gameFinished = true;
                MaterialDesignThemes.Wpf.DialogHost.Show(new DialogTemplate { Title = "You lost!", Content = "You hit a bomb" }, "RootDialog");
                TextBoxSeed.Text = "";
                return;
            }
            CheckWin();
        }

        /// <summary>
        /// Update cell image on undiscovered cells
        /// </summary>
        /// <param name="c">column index</param>
        /// <param name="r">row index</param>
        private void SetFlag(int c, int r) {
            string path = @"assets\";
            switch (boardDiscovered[c, r]) {
                case 0:
                    path += "flag.png";
                    boardDiscovered[c, r] = 2;
                    break;

                case 1:
                    return;

                case 2:
                    path += "unknown.png";
                    boardDiscovered[c, r] = 0;
                    break;
            }

            SetBombText();

            GridPlayArea.Children.OfType<Rectangle>().First(e => Grid.GetRow(e) == r && Grid.GetColumn(e) == c).Fill = new ImageBrush() {
                ImageSource = new BitmapImage(new Uri(path, UriKind.Relative))
            };
        }

        /// <summary>
        /// Update how many bombs there are left
        /// </summary>
        /// <param name="won">set bombs to 0</param>
        private void SetBombText(bool won = false) {
            if (won) {
                TextBombCounter.Text = "Bombs Left: 0";
                return;
            }

            int count = 0;
            foreach (var item in boardDiscovered) {
                if (item == 2) {
                    count++;
                }
            }

            TextBombCounter.Text = $"Bombs Left: {bombCount - count}";
        }

        /// <summary>
        /// Sets the cell image based on what it contains
        /// </summary>
        /// <param name="c">column index</param>
        /// <param name="r">row index</param>
        /// <param name="applyOutline">apply red tint</param>
        private void SetCellImage(int c, int r, bool applyOutline = false) {
            Rectangle rect = GridPlayArea.Children.OfType<Rectangle>().First(e => Grid.GetRow(e) == r && Grid.GetColumn(e) == c);

            string path = @"assets\";

            switch (boardArray[c, r]) {
                case 10:
                    path += $"mine{(applyOutline ? "_" : "")}.png";
                    break;

                case 0:
                    path += $"empty{(applyOutline ? "_" : "")}.png";
                    break;

                default:
                    path += $"{boardArray[c, r]}{(applyOutline ? "_" : "")}.png";
                    break;
            }

            rect.Fill = new ImageBrush() {
                ImageSource = new BitmapImage(new Uri(path, UriKind.Relative))
            };
        }

        /// <summary>
        /// Check if game has been won. If it has, stop timer and event game loop
        /// </summary>
        /// <returns>game won?</returns>
        private bool CheckWin() {
            int totalUndiscovered = 0;
            foreach (var item in boardDiscovered) {
                if (item != 1) {
                    totalUndiscovered++;
                }
            }

            if (totalUndiscovered == bombCount) {
                dispatcherTimer.Stop();
                RecordRun();
                gameFinished = true;
                SetBombText(true);
                SetPastRunsListBox();
                MaterialDesignThemes.Wpf.DialogHost.Show(new DialogTemplate { Title = "You Win!", Content = "You discovered all of the bombs" }, "RootDialog");
                TextBoxSeed.Text = "";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Records the run and stores it locally
        /// </summary>
        private void RecordRun() {
            if (!PastRuns.runs.ContainsKey(difficulties[gameIndex].Item1)) {
                PastRuns.runs[difficulties[gameIndex].Item1] = new List<Run>();
            }

            PastRuns.runs[difficulties[gameIndex].Item1].Add(new Run() { Time = runTime, DateStarted = DateTime.Now - runTime, SeedWasRandom = seedWasRandom, Seed = currentSeed, Difficulty = difficulties[gameIndex].Item1 });
            PastRuns.SaveRuns();
        }

        #endregion game logic
    }
}