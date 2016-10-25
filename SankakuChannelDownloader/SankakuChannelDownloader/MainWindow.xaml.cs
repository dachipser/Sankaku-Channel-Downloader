﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SankakuChannelAPI;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;

namespace SankakuChannelDownloader
{
    public partial class MainWindow : Window
    {
        public const string SavePath = "save.data";
        public static SankakuChannelUser User;
        public static bool CancelRequested = false;

        public List<DateTime> RegisteredTimestamps;
        public List<Log> Logs = new List<Log>();

        public MainWindow()
        {
            InitializeComponent();
         
            // Opens up the LoginWindow (check "LoginWindow.xaml.cs" for more info)
            LoginWindow form = new LoginWindow();
            form.ShowDialog();

            // If login was not successful - exit the program
            if (form.Success == false)
            {
                Environment.Exit(1);
                return;
            }

            // Subscribe to the event "FinishedWork"
            FinishedWork += MainWindow_FinishedWork;

            // Load saved data if it exists
            LoadData();

            // Display other necessary information...
            txtLoggedIn.Text = "Logged in as ";
            txtLoggedIn.Inlines.Add(new Run(User.Username) { FontWeight = FontWeights.Bold });

            txtTags.Focus(); // <-- focus on textbox "txtTags" so the user can start typing right away :D
        }

        private void LoadData()
        {
            try
            {
                // This just loads the save data if it exists, otherwise it does nothing
                if (File.Exists(SavePath) == false) return;
                Save sv = Save.GetSave(File.ReadAllBytes(SavePath));

                Left = sv.Left;
                Top = sv.Top;
                Height = sv.Height;
                Width = sv.Width;
                txtTags.Text = sv.Tags;
                txtBlacklist.Text = sv.BlacklistedTags;
                txtSizeLimit.Text = sv.SizeLimit;
                txtImageCount.Text = sv.ImageLimit;
                txtPath.Text = sv.FilePath;
                txtPageLimit.Text = (sv.PageLimit.Length == 0 || sv.PageLimit == "0") ? "20" : sv.PageLimit;
                this.WindowState = sv.IsFullscreen ? WindowState.Maximized : WindowState.Normal;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to read the save file!\n\n" + ex.Message, "Save file error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_FinishedWork(Object sender, DownloadStats e)
        {
            // This is what happens when that event "FinishedWork" (a little below) is invoked
            Dispatcher.Invoke(() =>
            {
                if (e.WasCancelled == false) WriteToLog("Finished task.");
                else WriteToLog("Task was cancelled.");

                MessageBox.Show($"Download {(e.WasCancelled ? "was cancelled." : "finished.")}\n\n" +
                    $"A total of {e.PostsFound} posts was found and {e.PostsDownloaded} posts were downloaded.",
                    "Download info", MessageBoxButton.OK, MessageBoxImage.Information);

                ToggleControls(true);
                btnStartDownload.Content = "Start Download";

                CancelRequested = false;
                btnStartDownload.IsEnabled = true;              
            });
        }

        private void txtPath_MouseDown(Object sender, MouseButtonEventArgs e)
        {
            // If you click on the Path text, a dialog opens to browse folders...
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.ShowDialog();

            if (dialog.SelectedPath.Length > 2)  // if a folder is selected, display it
            {
                txtPath.Text = dialog.SelectedPath;
            }
        }

        private void btnStartDownload_Click(Object sender, RoutedEventArgs e)
        {
            if (btnStartDownload.Content.ToString() == "Stop Download")
            {
                CancelRequested = true;
                btnStartDownload.IsEnabled = false;
                WriteToLog("User requested to abort the task... please wait.");
            }
            else
            {
                // A hell lot of validation going on here...
                if (txtTags.Text.Length < 3)
                {
                    MessageBox.Show("Please enter some actual tags.", "Invalid tags", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                // Getting text from TextBoxes -- removing excessive spaces etc...
                string tags = Regex.Replace(txtTags.Text, @"\s+", " ");
                string blacklisted = Regex.Replace(txtBlacklist.Text, @"\s+", " ");
                if (blacklisted == " ") blacklisted = "";
                if (tags == " ") tags = "";
                if (tags.StartsWith(" ")) tags = tags.Substring(1, tags.Length - 1);

                int count;
                double sizeLimit;
                int pageLimit = 20;
                int startingPage = 1;
                bool skipExisting = checkBoxSkip.IsChecked == true;
                bool containVideos = checkboxFilterVideos.IsChecked == false;

                // MORE VALIDATION....
                bool isUserIdiot = false;
                foreach (var tg in tags.Split(' '))
                {
                    foreach (var b in txtBlacklist.Text.Split(' '))
                        if (tg.ToLower() == b.ToLower())
                        {
                            isUserIdiot = true;
                            break;
                        }

                    if (isUserIdiot) break;
                }
                if (isUserIdiot)
                {
                    MessageBox.Show("Are you a fcken idiot?\nDon't use the same search tags for the blacklist!", "User is an idiot", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                if (int.TryParse(txtStartingPage.Text, out startingPage) == false || startingPage <= 0)
                {
                    MessageBox.Show("You can't start searching at page 0 or less!", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                if (int.TryParse(txtPageLimit.Text, out pageLimit) == false || pageLimit < 1)
                {
                    MessageBox.Show("Please explain to me how the hell are you going to find any post AT ALL\n if you're searching for less than 0 posts per page.\n\nHa, genius?", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                if (txtBlacklist.Text.Contains(':'))
                {
                    MessageBox.Show("The blacklist contains an invalid tag.", "Invalid tag", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                if (int.TryParse(txtImageCount.Text, out count) == false || count < 0)
                {
                    MessageBox.Show("Invalid number of images entered!", "Invalid number", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                if (double.TryParse(txtSizeLimit.Text, out sizeLimit) == false || sizeLimit < 0)
                {
                    MessageBox.Show("Invalid size limit entered!", "Invalid number", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                if (Directory.Exists(txtPath.Text) == false)
                {
                    MessageBox.Show("Invalid directory specified!", "Invalid path!", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
                string path = txtPath.Text;

                // Prompt if user is sure to continue...
                if (MessageBox.Show("Are you sure you wish to start the download process?\n", "Are you sure?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No) return;

                ToggleControls(false);
                btnStartDownload.Content = "Stop Download";

                // Start the task - give it ALL the information it needs... now that's a lot of parameters... damn.
                Task.Run(() => StartDownloading(tags, count, path, sizeLimit, blacklisted, containVideos, startingPage, pageLimit, skipExisting));             
            }
        }

        private void ToggleControls(bool state)
        {
            txtTags.IsEnabled = state;
            txtPath.IsEnabled = state;
            txtImageCount.IsEnabled = state;
            txtSizeLimit.IsEnabled = state;
            txtBlacklist.IsEnabled = state;
            checkboxFilterVideos.IsEnabled = state;
            checkBoxSkip.IsEnabled = state;
            txtPageLimit.IsEnabled = state;
            txtStartingPage.IsEnabled = state;
        }
        public void WriteToLog(string msg, bool registerTime = false, string filename = "", bool isError = false, string exMessage = "", string[] fndPosts = null)
        {           
            // Dispatcher needs to be called when interacting with the GUI - otherwise an error can be thrown
            Dispatcher.Invoke(() =>
            {
                var date = DateTime.Now;
                if (registerTime) RegisteredTimestamps.Add(date);
                Logs.Add(new Log($"[{date.ToString("HH:mm:ss")}] " + msg, date, filename, isError, exMessage, fndPosts));  // <--- adding the log to my log collection

                listBox.ItemsSource = Logs;  // <-- displaying my log collection
                listBox.Items.Refresh();     // <-- refreshing the view
                listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);  // <-- scrolling to the end, so the viewer sees the latest log
            });
        }

        public void UpdateETA(DownloadStats stats, bool onlyFound = false, bool finished = false)
        {
            Dispatcher.Invoke(() =>
            {
                txtETA.Inlines.Clear();
                txtETA.Inlines.Add(new Run("Remaining (ETA): "));
                if (RegisteredTimestamps.Count < 3) txtETA.Inlines.Add(new Run($"{(finished ? "-" : (onlyFound ? "Not yet downloading." : $"{"Calculating..."}"))}") { FontWeight = FontWeights.Bold });
                else
                {
                    int count = 0;
                    int toScan = (stats.PostsFound > 60) ? 60 : stats.PostsFound;
                    double totalMiliseconds = 0.0;
                    for (int i = RegisteredTimestamps.Count - 1; i >= RegisteredTimestamps.Count - 1 - toScan; i--)
                    {
                        if (i < 1) break;
                        totalMiliseconds += RegisteredTimestamps[i].Subtract(RegisteredTimestamps[i - 1]).TotalMilliseconds;
                        count++;
                    }
                    double averageTime = totalMiliseconds / count;
                    double ETA = averageTime * (stats.PostsFound - stats.PostsDownloaded);
                    TimeSpan span = TimeSpan.FromMilliseconds(ETA);

                    txtETA.Inlines.Add(
                        new Run($"" +
                        $"{(finished ? "-" : $"{((span.TotalMinutes < 1) ? $"{span:ss} seconds" : ((span.TotalHours < 1) ? $"{span:mm} minutes, {span:ss} seconds" : ((span.TotalDays < 1) ? $"{span:hh} hours, {span:mm} minutes, {span:ss} seconds" : $"{span:d} {((span.TotalDays < 2) ? "day" : "days")} {span:hh} hours")))}")}")
                        { FontWeight = FontWeights.Bold });
                }
            });
        }

        public event EventHandler<DownloadStats> FinishedWork;  // This event handler gets invoked when Task is either finished or cancelled
        public static int SecondsWaited = 0; // This is just a temporary variable that gets incremented when task needs to wait for something....

        private void StartDownloading(string tags, int imageLimit, string path, double sizeLimit, string blacklistedTags, bool containVideos, int pageCount, 
            int limit, bool skipExisting = false)
        {
            DownloadStats stats = new DownloadStats();
            WriteToLog($"Task started");
            RegisteredTimestamps = new List<DateTime>(); UpdateETA(stats, true);

            List<SankakuPost> foundPosts = new List<SankakuPost>();
            string[] blTags = blacklistedTags.Split(' ');
            WriteToLog($"Searching for posts in chunks of {limit} posts per page...");
            while (true)
            {
                #region Searching posts
                search:
                if (CancelRequested)
                {
                    // Task gets cancelled if cancel is requested
                    stats.WasCancelled = true;
                    UpdateETA(stats, true, true);
                    FinishedWork?.Invoke(null, stats);
                    return;
                }

                try
                {
                    var list = User.Search(tags, pageCount, limit);
                    stats.PostsFound += list.Count;

                    // remove posts with blacklisted tags       
                    int removed = 0;
                    if (blTags.Length > 0)
                        foreach (string s in blTags)
                            removed += list.RemoveAll(x =>
                            {
                                foreach (var t in x.Tags)
                                {
                                    if (t.ToLower() == s) return true;
                                }

                                return false;
                            });

                    foundPosts.AddRange(list);
                    WriteToLog($"Found {list.Count} posts on page {pageCount}.{(removed > 0 ? $" (Removed {removed} posts because of blacklisted tags)" : "")}", fndPosts: list.Select(x => x.PostReference).ToArray());

                    if (foundPosts.Count >= imageLimit && imageLimit > 0) break;
                    if (list.Count < 2) break;
                    pageCount++;
                    SecondsWaited = 0;
                }
                catch (WebException ex)
                {
                    // Error handling... a lot of shit going on in here...
                    #region Error handling
                    if (ex.Message.ToLower().Contains("too many requests"))
                    {
                        if (SecondsWaited == 0) WriteToLog("Too many requests", isError: true, exMessage: ex.Message);

                        if (SecondsWaited < 60) SecondsWaited += 15;
                        else if (SecondsWaited >= 60 && SecondsWaited < 60 * 15) SecondsWaited += 120;

                        WriteToLog($"Retrying in {SecondsWaited} seconds...");
                        Thread.Sleep(SecondsWaited * 1000);
                        goto search;
                    }
                    else if (ex.Message.ToLower().Contains("remote name could not be resolved"))
                    {
                        WriteToLog("Internet connection lost. Waiting for internet...", isError: true, exMessage: ex.Message);

                        int secondsToWait = 2;
                        while (true)
                        {
                            if (CancelRequested)
                            {
                                stats.WasCancelled = true;
                                UpdateETA(stats, true, true);
                                FinishedWork?.Invoke(null, stats);
                                return;
                            }

                            Thread.Sleep(secondsToWait * 1000);
                            try
                            {
                                using (var client = new WebClient())
                                {
                                    using (var stream = client.OpenRead("http://www.google.com"))
                                    {
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                if (secondsToWait < 60 * 10)
                                    secondsToWait += 1;
                            }
                        }

                        WriteToLog("Internet connection restored. Continuing task...");
                        goto search;
                    }
                    else WriteToLog("ERROR: " + ex.Message, isError: true, exMessage: ex.Message);
                    #endregion
                } 
                #endregion
            }            

            // remove posts to fit the limit
            if (foundPosts.Count > imageLimit && imageLimit > 0)
            {
                int removed = 0;
                do
                {
                    foundPosts.RemoveAt(foundPosts.Count - 1);
                    removed++;
                } while (foundPosts.Count > imageLimit);

                stats.PostsFound -= removed;
                WriteToLog($"Removed {removed} found posts to fit the given limit.");
            }
            
            WriteToLog($"Found all posts. ({foundPosts.Count} posts found in total)");
            WriteToLog("Downloading images...");

            var files = (skipExisting) ? Directory.GetFiles(path) : null;
            foreach (var a in foundPosts)
            {
                download:
                try
                {
                    #region Download posts
                    // Check if cancel requested
                    if (CancelRequested)
                    {
                        stats.WasCancelled = true;
                        UpdateETA(stats, true, true);
                        FinishedWork?.Invoke(null, stats);
                        return;
                    }

                    // Check for existing images
                    if (skipExisting)
                        if (ImageExists(a.PostID, files, out string filename))
                        {
                            double procentage = ((double)(foundPosts.IndexOf(a) + 1) / (double)foundPosts.Count) * 100;

                            WriteToLog($"[{procentage.ToString("0.000") + "%",-10}] Skipped existing file \"{filename}\"", true, filename);
                            stats.PostsDownloaded++;
                            continue;
                        }

                    // Download actual image
                    var imageLink = a.GetFullImageLink();
                    var data = a.DownloadFullImage(imageLink, out bool wasRedirected, containVideos, sizeLimit);

                    // Check if response was redirected
                    if (wasRedirected == false)
                    {
                        // Check if post was too big/is a video file
                        if (data == null)
                        {
                            WriteToLog($"The post '{a.PostID}' was skipped because of given conditions.");
                            continue;
                        }

                        // Determine which extension to use
                        var extension = new Regex(@".*?\.([jpg,gif,png,jpeg,webm,mp4,bmp]*?)\?", RegexOptions.IgnoreCase).Match(imageLink).Groups[1].Value;
                        string filename = $"{path}\\{a.PostID}.{extension}";  // <-- generate filename
                        File.WriteAllBytes(filename, data);

                        // Display progress
                        double procentage = ((double)(foundPosts.IndexOf(a) + 1) / (double)foundPosts.Count) * 100;
                        WriteToLog($"[{procentage.ToString("0.000") + "%",-10}] Downloaded \"{filename}\"", true, filename);
                        UpdateETA(stats);

                        stats.PostsDownloaded++;
                        SecondsWaited = 0;
                    }
                    else
                    {
                        WriteToLog($"Server response was redirected!");
                    } 
                    #endregion
                }
                catch (WebException ex)
                {
                    // Error handling... a lot of shit going on in here as well
                    #region Error handling
                    if (ex.Message.ToLower().Contains("too many requests"))
                    {
                        #region Too Many Requests
                        if (SecondsWaited == 0) WriteToLog("Too many requests", isError: true, exMessage: ex.Message);

                        if (SecondsWaited < 60) SecondsWaited += 15;
                        else if (SecondsWaited >= 60 && SecondsWaited < 60 * 15) SecondsWaited += 120;
                            
                        WriteToLog($"Retrying in {SecondsWaited} seconds...");
                        Thread.Sleep(SecondsWaited * 1000);
                        goto download;
                        #endregion
                    }
                    else if (ex.Message.ToLower().Contains("remote name could not be resolved"))
                    {
                        #region No Internet
                        WriteToLog("Internet connection lost. Waiting for internet...", isError: true, exMessage: ex.Message);

                        int secondsToWait = 2;
                        while (true)
                        {
                            if (CancelRequested)
                            {
                                stats.WasCancelled = true;
                                UpdateETA(stats, true, true);
                                FinishedWork?.Invoke(null, stats);
                                return;
                            }

                            Thread.Sleep(secondsToWait * 1000);
                            try
                            {
                                using (var client = new WebClient())
                                {
                                    using (var stream = client.OpenRead("http://www.google.com"))
                                    {
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                if (secondsToWait < 60 * 10)
                                    secondsToWait += 1;
                            }
                        }

                        WriteToLog("Internet connection restored. Continuing task...");
                        goto download; 
                        #endregion
                    }
                    else if (ex.Message.ToLower().Contains("time") && ex.Message.ToLower().Contains("out"))
                    {
                        #region Timeout
                        WriteToLog("ERROR: " + ex.Message, isError: true, exMessage: ex.Message);
                        WriteToLog("Attempting to restore the connection...");

                        while (true)
                        {
                            if (CancelRequested)
                            {
                                stats.WasCancelled = true;
                                UpdateETA(stats, true, true);
                                FinishedWork?.Invoke(null, stats);
                                return;
                            }

                            bool success = false;
                            try
                            {
                                success = LoginWindow.LoginUser(User, true);
                            }
                            catch { }

                            if (success)
                            {
                                WriteToLog("Successfully restored connection. Continuing task...");
                                goto download;
                            }
                            else
                            {
                                if (SecondsWaited < 600) SecondsWaited += 15;

                                WriteToLog($"Failed to establish connection. Attempting again in {SecondsWaited} seconds...");
                                Thread.Sleep(SecondsWaited * 1000);
                            }
                        } 
                        #endregion
                    }
                    else WriteToLog("ERROR: " + ex.Message, isError: true, exMessage: ex.Message);
                    #endregion
                }
                catch (Exception ex)
                {
                    WriteToLog("ERROR: " + ex.Message, isError: true, exMessage: ex.Message); 
                }
            }

            UpdateETA(stats, true, true);
            FinishedWork?.Invoke(null, stats);
        }

        private bool ImageExists(int postID, string[] files, out string filename)
        {
            foreach(var s in files)
            {
                if (s.ToLower().Contains(postID.ToString()))
                {
                    filename = s;
                    return true;
                }
            }

            filename = "";
            return false;
        }

        private void txtImageCount_GotFocus(Object sender, RoutedEventArgs e) => ((TextBox)sender).SelectAll();

        private void listBox_MouseDoubleClick(Object sender, MouseButtonEventArgs e)
        {
            // If nothing is selected, then return
            if (listBox.SelectedIndex == -1) return;

            // ... otherwise get the selected Log
            Log log = (Log)listBox.SelectedItem;

            // if it's a picture, open it, using the default program
            if (log.DownloadedFilepath.Length > 1)
            {
                try
                {
                    Process.Start(log.DownloadedFilepath);  
                }
                catch(Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (log.IsError)
            {
                // if it's an error - show more information
                MessageBox.Show("Showing the logged exception message:\n\n" + log.ErrorMessage, "Exception message", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (log.FoundPosts != null)
            {
                // show
                PostInfo form = new PostInfo(log.FoundPosts);
                form.ShowDialog();
            }
        }

        private void Window_Closing(Object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Save data when window closes
                File.WriteAllBytes(SavePath, new Save() {
                    Tags = txtTags.Text,
                    BlacklistedTags = txtBlacklist.Text,
                    ImageLimit = txtImageCount.Text,
                    SizeLimit = txtSizeLimit.Text,
                    FilePath = txtPath.Text,
                    Left = this.Left,
                    Top = this.Top,
                    Width = this.Width,
                    Height = this.Height,
                    IsFullscreen = this.WindowState == WindowState.Maximized,
                    PageLimit = txtPageLimit.Text,
                }.GetBytes());
            }
            catch(Exception ex)
            {
                MessageBox.Show("Failed to create a save file!\n\n" + ex.Message, "Save file error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // These below are serializable classes that can be converted into bytes and written to disc
    [Serializable]
    public class Log
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public bool IsError { get; set; }
        public string ErrorMessage { get; set; }
        public string DownloadedFilepath { get; set; }
        public string[] FoundPosts { get; set; }

        public Log(string Message, DateTime timestamp, string filename = "", bool isError = false, string errMsg = "", string[] foundPosts = null)
        {
            this.Timestamp = timestamp;
            this.Message = Message;
            this.IsError = isError;
            this.DownloadedFilepath = filename;
            this.ErrorMessage = errMsg;
            this.FoundPosts = foundPosts;
        }
    }

    [Serializable]
    public class Save
    {
        public string Tags { get; set; }
        public string BlacklistedTags { get; set; }
        public string ImageLimit { get; set; }
        public string SizeLimit { get; set; }
        public bool ContainVideo { get; set; }
        public string FilePath { get; set; }
        public string PageLimit { get; set; }


        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsFullscreen { get; set; }

        public byte[] GetBytes()
        {
            // This shit will convert THIS object to bytes
            using (MemoryStream stream = new MemoryStream())
            {
                new BinaryFormatter().Serialize(stream, this);
                return stream.ToArray();
            }
        }
        public static Save GetSave(byte[] source)
        {
            // This shit, however, will convert bytes BACK into this object
            using (MemoryStream stream = new MemoryStream(source))
            {
                return (Save)new BinaryFormatter().Deserialize(stream);             
            }
        }
    }
}