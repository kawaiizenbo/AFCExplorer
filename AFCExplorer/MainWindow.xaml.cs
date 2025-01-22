using iMobileDevice;
using iMobileDevice.Afc;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;
using iMobileDevice.Plist;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Timers;
using System.Windows;
using System.Windows.Input;

namespace AFCExplorer
{
    public partial class MainWindow : Window
    {
        private readonly IiDeviceApi idevice = LibiMobileDevice.Instance.iDevice;
        private readonly ILockdownApi lockdown = LibiMobileDevice.Instance.Lockdown;
        public static IAfcApi afc = LibiMobileDevice.Instance.Afc;

        private iDeviceHandle deviceHandle;
        private LockdownClientHandle lockdownHandle;
        public static LockdownServiceDescriptorHandle lockdownServiceHandle;
        public static AfcClientHandle afcHandle;

        private string deviceType;
        private string deviceName;
        private string deviceVersion;
        private string deviceUDID = "";
        private bool gotDeviceInfo;

        private readonly Timer deviceDetectorTimer;

        string path = "/";
        bool afc2 = false;

        public MainWindow()
        {
            InitializeComponent();
            NativeLibraries.Load();
            deviceDetectorTimer = new Timer
            {
                Interval = 1000
            };
            deviceDetectorTimer.Elapsed += Event_deviceDetectorTimer_Tick;
        }

        private void ReadDirectory()
        {
            try
            {
                if (!path.EndsWith("/")) throw new Exception();
                afc.afc_read_directory(afcHandle, path, out ReadOnlyCollection<string> tempFilesList).ThrowOnError();
                List<AFCFileType> afcDirectory = new List<AFCFileType>();
                foreach (string fn in tempFilesList)
                {
                    if (fn == "." || fn == "..") continue;
                    afc.afc_get_file_info(afcHandle, path + fn, out ReadOnlyCollection<string> fileInfo).ThrowOnError();
                    string readableSize = FormatBytes(ulong.Parse(fileInfo[1]));
                    string readableType = fileInfo[7] == "S_IFDIR" ? "Directory" : "File";
                    int unixSeconds = (int)(((long.Parse(fileInfo[9]) / 1000) / 1000) / 1000);
                    string readableTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).DateTime.ToString();
                    afcDirectory.Add(new AFCFileType(fn, readableType, readableTime, readableSize));
                }
                DirectoryItems.ItemsSource = afcDirectory;
                PathTextBox.Text = path;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                path = "/";
                if (afc2) path = "/private/var/mobile/Media/";
                ReadDirectory();
            }
        }

        private void ChangeTransportControlState(bool enabled)
        {
            RefreshButton.IsEnabled = enabled;
            HomeButton.IsEnabled = enabled;
            PathTextBox.IsEnabled = enabled;
            GoButton.IsEnabled = enabled;
            DownloadFileButton.IsEnabled = enabled;
            UploadFileButton.IsEnabled = enabled;
            MakeDirectoryButton.IsEnabled = enabled;
            DeleteButton.IsEnabled = enabled;
            ConnectAFC2Button.IsEnabled = enabled;
            GoUpButton.IsEnabled = enabled;
            if (!enabled) DirectoryItems.ItemsSource = null;
        }

        private void Event_window_Loaded(object sender, RoutedEventArgs e)
        {
            ChangeTransportControlState(false);
            deviceDetectorTimer.Start();
        }

        private void Event_deviceDetectorTimer_Tick(object sender, EventArgs e)
        {
            int count = 0;
            if (idevice.idevice_get_device_list(out ReadOnlyCollection<string> udids, ref count) == iDeviceError.NoDevice || count == 0)
            {
                // found nothing
                deviceUDID = "";

                Dispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(
                delegate ()
                {
                    window.Title = "AFCExplorer (No Device)";
                }));

                if (gotDeviceInfo)
                {
                    // device was connected and now it isnt, let user know
                    Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Normal,
                    new Action(
                    delegate ()
                    {
                        ChangeTransportControlState(false);
                        StatusLabel.Content = "Device disconnected.";
                        ConnectAFC2Button.Content = "Enable AFC2";
                        PathTextBox.Text = "/";
                    }));
                    gotDeviceInfo = false;
                    afc2 = false;
                    path = "/";
                }
            }
            else
            {
                if (!gotDeviceInfo)
                {
                    try
                    {
                        Dispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Normal,
                        new Action(
                        delegate ()
                        {
                            StatusLabel.Content = "Found device, attempting to connect.";
                        }));
                        idevice.idevice_new(out deviceHandle, udids[0]).ThrowOnError();
                        lockdown.lockdownd_client_new_with_handshake(deviceHandle, out lockdownHandle, "AFCExplorer").ThrowOnError();

                        // make afc connection
                        lockdown.lockdownd_start_service(lockdownHandle, "com.apple.afc", out lockdownServiceHandle);
                        lockdownHandle.Api.Afc.afc_client_new(deviceHandle, lockdownServiceHandle, out afcHandle);

                        // get device info
                        lockdown.lockdownd_get_device_name(lockdownHandle, out deviceName).ThrowOnError();
                        lockdown.lockdownd_get_value(lockdownHandle, null, "ProductVersion", out PlistHandle temp).ThrowOnError();
                        temp.Api.Plist.plist_get_string_val(temp, out deviceVersion);
                        lockdown.lockdownd_get_value(lockdownHandle, null, "ProductType", out temp).ThrowOnError();
                        temp.Api.Plist.plist_get_string_val(temp, out deviceType);

                        temp.Dispose();

                        deviceUDID = udids[0];

                        Dispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Normal,
                        new Action(
                        delegate ()
                        {
                            window.Title = $"AFCExplorer ({deviceName}, {deviceType}, iOS {deviceVersion})";
                            StatusLabel.Content = "Connected to device.";
                            PathTextBox.Text = path;
                            ChangeTransportControlState(true);
                            ReadDirectory();
                        }));
                        gotDeviceInfo = true;
                    }
                    catch (Exception ex)
                    {
                        // fucked it up in some way
                        MessageBox.Show(ex.Message);
                        deviceUDID = "";
                        Dispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Normal,
                        new Action(
                        delegate ()
                        {
                            window.Title = "AFCExplorer (No Device)";
                            StatusLabel.Content = "Failed to connect to device.";
                            ChangeTransportControlState(false);
                        }));

                        gotDeviceInfo = false; // never should matter but just in case
                    }
                }
            }
        }

        private void GoButton_Click(object sender, RoutedEventArgs e)
        {
            path = PathTextBox.Text;
            ReadDirectory();
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            // these go to the same place
            path = "/";
            if (afc2) path = "/private/var/mobile/Media/";
            ReadDirectory();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            ReadDirectory();
        }

        private void ConnectAFC2Button_Click(object sender, RoutedEventArgs e)
        {
            // disable afc2 if already enabled
            if (afc2)
            {
                lockdown.lockdownd_start_service(lockdownHandle, "com.apple.afc", out lockdownServiceHandle).ThrowOnError();
                lockdownHandle.Api.Afc.afc_client_new(deviceHandle, lockdownServiceHandle, out afcHandle).ThrowOnError();
                afc2 = false;
                ConnectAFC2Button.Content = "Enable AFC2";
                path = "/";
                ReadDirectory();
            }
            else
            {
                try
                {
                    lockdown.lockdownd_start_service(lockdownHandle, "com.apple.afc2", out lockdownServiceHandle).ThrowOnError();
                    lockdownHandle.Api.Afc.afc_client_new(deviceHandle, lockdownServiceHandle, out afcHandle).ThrowOnError();
                    afc2 = true;
                    ConnectAFC2Button.Content = "Disable AFC2";
                    path = "/private/var/mobile/Media/";
                    ReadDirectory();
                }
                catch (Exception)
                {
                    MessageBox.Show("AFC2 Connection failed!\nBe sure your device is jailbroken and has the Apple File Conduit 2 tweak installed.");
                    afc2 = false;
                }
            }
        }

        private void MakeDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            GenericSingleInputWindow inputDialog = new GenericSingleInputWindow();
            inputDialog.Title = "Name of directory.";
            inputDialog.TextBox.Text = "New Folder";
            inputDialog.ShowDialog();
            if (inputDialog.result == System.Windows.Forms.DialogResult.Cancel) return;
            afc.afc_make_directory(afcHandle, path + inputDialog.TextBox.Text);
            ReadDirectory();
        }

        private void UploadFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openAfcUploadFile = new Microsoft.Win32.OpenFileDialog();

            openAfcUploadFile.ShowDialog();

            if (openAfcUploadFile.FileName == "") return;

            string afcUploadFilePath = openAfcUploadFile.FileName;

            string afcUploadFileName = afcUploadFilePath.Split('\\').Last();

            ulong handle = 0UL;
            afc.afc_file_open(afcHandle, path + "/" + afcUploadFileName, AfcFileMode.FopenRw, ref handle);
            byte[] array = File.ReadAllBytes(afcUploadFilePath);
            uint bytesWritten = 0U;
            afc.afc_file_write(afcHandle, handle, array, (uint)array.Length, ref bytesWritten);
            afc.afc_file_close(afcHandle, handle);
            ReadDirectory();
        }

        private void DownloadFileButton_Click(object sender, RoutedEventArgs e)
        {
            var saveAfcFile = new Microsoft.Win32.SaveFileDialog
            {
                FileName = ((AFCFileType)DirectoryItems.SelectedItem).Name
            };

            saveAfcFile.ShowDialog();

            string afcSaveFilePath = saveAfcFile.FileName;
            string afcFilePath = path + ((AFCFileType)DirectoryItems.SelectedItem).Name;
            afc.afc_get_file_info(afcHandle, afcFilePath, out ReadOnlyCollection<string> infoListr);
            List<string> infoList = new List<string>(infoListr.ToArray());
            long fileSize = Convert.ToInt64(infoList[infoList.FindIndex(x => x == "st_size") + 1]);

            ulong fileHandle = 0;
            afc.afc_file_open(afcHandle, afcFilePath, AfcFileMode.FopenRdonly, ref fileHandle);

            FileStream fileStream = File.Create(afcSaveFilePath);
            const int bufferSize = 4194304;
            for (int i = 0; i < fileSize / bufferSize + 1; i++)
            {
                uint bytesRead = 0;

                long remainder = fileSize - i * bufferSize;
                int currBufferSize = remainder >= bufferSize ? bufferSize : (int)remainder;
                byte[] currBuffer = new byte[currBufferSize];

                if (afc.afc_file_read(afcHandle, fileHandle, currBuffer, Convert.ToUInt32(currBufferSize), ref bytesRead)
                    != AfcError.Success)
                {
                    afc.afc_file_close(afcHandle, fileHandle);
                }

                fileStream.Write(currBuffer, 0, currBufferSize);
            }

            fileStream.Close();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                afc.afc_remove_path(afcHandle, path + $"/{DirectoryItems.SelectedItem}").ThrowOnError();
                ReadDirectory();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Failed to delete item.");
            }
        }

        private void DirectoryItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DirectoryItems.SelectedItem == null) return;
            if (((AFCFileType)DirectoryItems.SelectedItem).Type == "File") return;
            path += $"{((AFCFileType)DirectoryItems.SelectedItem).Name}/";
            ReadDirectory();
        }

        private void GoUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (path == "/") return;
            path = path.Replace('/' + path.TrimEnd('/').Split('/').Last(), "");
            ReadDirectory();
        }

        public static string FormatBytes(ulong bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1000; i++, bytes /= 1000)
            {
                dblSByte = bytes / 1000.0;
            }

            return string.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }
    }
}
