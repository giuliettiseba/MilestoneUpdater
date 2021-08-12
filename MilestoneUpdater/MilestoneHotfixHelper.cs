﻿using ConfigApi;
using MilestoneHotfixHelper.Utils;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;
using VideoOS.ConfigurationAPI;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;
using VideoOS.Platform.SDK.Platform;

namespace MilestoneUpdater
{
    public partial class MilestoneHotfixHelper : Form
    {

        // Join the dark theme 
        readonly Color TEXTBACKCOLOR = System.Drawing.ColorTranslator.FromHtml("#252526");
        readonly Color BACKCOLOR = System.Drawing.ColorTranslator.FromHtml("#2D2D30");
        readonly Color INFOCOLOR = System.Drawing.ColorTranslator.FromHtml("#1E7AD4");
        readonly Color MESSAGECOLOR = System.Drawing.ColorTranslator.FromHtml("#86A95A");
        readonly Color DEBUGCOLOR = System.Drawing.ColorTranslator.FromHtml("#DCDCAA");
        readonly Color ERRORCOLOR = System.Drawing.ColorTranslator.FromHtml("#B0572C");


        // SDK info
        private static readonly Guid IntegrationId = new Guid("CD52BF80-A58B-4A35-BF30-159753159753");
        private const string IntegrationName = "MilestoneUpdater";
        private const string Version = "1.0";
        private const string ManufacturerName = "SGIU";


        // Folders 

        private const string HOTFIXLIST = "https://download.milestonesys.com/sgiu/hotfixHelper/hotfixList.json";

        public MilestoneHotfixHelper()
        {
            InitializeComponent();

            this.textBox_Console.BackColor = TEXTBACKCOLOR;
            this.BackColor = BACKCOLOR;
            this.groupBox1.BackColor = BACKCOLOR;
            this.groupBox2.BackColor = BACKCOLOR;
            this.groupBox3.BackColor = BACKCOLOR;
            this.groupBox4.BackColor = BACKCOLOR;


       //     TestParametersLocal();                    /// REMOVE ON PRODUCTION !!!!
        }

        private void TestParametersLocal()
        {
            textBoxMSAddress.Text = "172.18.190.238";
            textBoxMSDomain.Text = ".";
            textBoxMSUser.Text = "Administrator";
            textBoxMSPass.Text = "Milestone1$";

            textBoxAllDomain.Text = ".";
            textBoxAllUser.Text = "Administrator";
            textBoxAllPass.Text = "Milestone1$";
        }
        private void TestParameters()
        {
            textBoxMSAddress.Text = "10.1.0.192";
            textBoxMSDomain.Text = "MEX-LAB";
            textBoxMSUser.Text = "SGIU";
            textBoxMSPass.Text = "Milestone1$";

            textBoxAllDomain.Text = "MEX-LAB";
            textBoxAllUser.Text = "SGIU";
            textBoxAllPass.Text = "Milestone1$";
        }

        private static String LOCALFOLDER = @"c:\ProgramData\Milestone\HotfixInstaller";

        private static String REMOTEFOLDER = @"c:\MilestoneHotfix";
        private static String REMOTESHARENAME = "MilestoneHotfix";

        private int CallProcess(ServerInfo remoteInfo, string filePath, string file)
        {
            WriteInConsole("Start updater on " + remoteInfo.Address, LogType.message);

            ConnectionOptions theConnection = new ConnectionOptions();
            theConnection.Authority = "ntlmdomain:" + remoteInfo.Domain;
            theConnection.Username = remoteInfo.UserName;
            theConnection.Password = remoteInfo.Password;

            ManagementScope theScope = new ManagementScope("\\\\" + remoteInfo.Address + "\\root\\cimv2", theConnection);

            // create a share folder 
            // TODO Check if folder is already created 

            WriteInConsole("Creating Share Folder " + remoteInfo.Address, LogType.info);
            var Win32_Process_Class = new ManagementClass(theScope, new ManagementPath("Win32_Process"), new ObjectGetOptions());
            object[] cmdMdTemp = { "cmd.exe /c md " + REMOTEFOLDER };
            var mdResult = Win32_Process_Class.InvokeMethod("Create", cmdMdTemp);
            WriteInConsole("Create Share Folder " + ErrorCodeToString(Convert.ToInt32(mdResult)), LogType.info);

            // share the folder 

            // TODO Chech if folder is already shared 
            WriteInConsole("Sharing Folder " + remoteInfo.Address, LogType.info);
            var winShareClass = new ManagementClass(theScope, new ManagementPath("Win32_Share"), new ObjectGetOptions());
            ManagementBaseObject shareParams = SetShareParams(winShareClass, REMOTEFOLDER, REMOTESHARENAME);

            var outParams = winShareClass.InvokeMethod("Create", shareParams, null);
            WriteInConsole("Share Folder " + ShareFolderErrorCodeToString(Convert.ToInt32(outParams.Properties["ReturnValue"].Value)), LogType.info);


            // Copy the hotfix 
            try
            {
                string shareFolder = @"\\" + remoteInfo.Address + "\\" + REMOTESHARENAME;
                string srcFile = filePath + "\\" + file;

                WriteInConsole("Copying file" + srcFile + @" to " + shareFolder + "\\" + file, LogType.info);

                NetworkShare.DisconnectFromShare(shareFolder, true); //Disconnect in case we are currently connected with our credentials;

                NetworkShare.ConnectToShare(shareFolder, remoteInfo.Domain + "\\" + remoteInfo.UserName, remoteInfo.Password); //Connect with the new credentials

                File.Copy(srcFile, shareFolder + "\\" + file);

                NetworkShare.DisconnectFromShare(shareFolder, false); //Disconnect from the server.

                WriteInConsole("File Copied", LogType.info);
            }
            catch (System.IO.IOException ex)
            {
                WriteInConsole(ex.Message, LogType.error);
            }


            // RUN IT !!!! AND DO IT QUIET !!! 

            WriteInConsole("Executing Hotfix on server " + remoteInfo.Address, LogType.info);

            String quiet_and_silent = " /quiet /install";

            object[] theProcessToRun = { REMOTEFOLDER + "\\" + file + quiet_and_silent, null, null, 0 };

            ManagementClass theClass = new ManagementClass(theScope, new ManagementPath("Win32_Process"), new ObjectGetOptions());
            var output = theClass.InvokeMethod("Create", theProcessToRun);
            try
            {
                String ProcID = theProcessToRun[3].ToString();

                WriteInConsole("Execute Hotfix on server " + remoteInfo.Address + ": " + ErrorCodeToString(Convert.ToInt32(output)), LogType.info);

                WriteInConsole("PID on server  " + remoteInfo.Address + ": " + ProcID, LogType.debug);

                WriteInConsole("Installing hotfix on server: " + remoteInfo.Address, LogType.message);

                WqlEventQuery wQuery = new WqlEventQuery("Select * From __InstanceDeletionEvent Within 1 Where TargetInstance ISA 'Win32_Process'");

                using (ManagementEventWatcher wWatcher = new ManagementEventWatcher(theScope, wQuery))
                {
                    bool stopped = false;

                    while (stopped == false)
                    {
                        using (ManagementBaseObject MBOobj = wWatcher.WaitForNextEvent())
                        {
                            if (((ManagementBaseObject)MBOobj["TargetInstance"])["ProcessID"].ToString() == ProcID)
                            {
                                // the process has stopped
                                stopped = true;
                                WriteInConsole("Update Process Finished on server: " + remoteInfo.Address, LogType.message);
                                // Upgrade Finish 
                            }
                        }
                    }
                    wWatcher.Stop();
                }
            }
            catch (Exception ex)
            {
                WriteInConsole(ex.Message, LogType.error);
            }

            ///
            // TODO: CHECK INSTALATION 
            ///


            /// /// /// ///  /// 
            /// HOUSEKEEPING /// 
            /// /// /// ///  /// 

            // UNSHARE FOLDER 

            WriteInConsole("Unsharing Folder " + remoteInfo.Address, LogType.info);

            //var winUnshareClass = new ManagementClass(theScope, new ManagementPath("Win32_Share"), new ObjectGetOptions());
            //ManagementBaseObject unshareParams = SetShareParams(winShareClass, filepath, sharename);
            ManagementObjectCollection collection = winShareClass.GetInstances();
            foreach (ManagementObject item in collection)
            {
                if (Convert.ToString(item["Name"]).Equals(REMOTESHARENAME))
                {
                    var unshareOutParams = item.InvokeMethod("Delete", new object[] { });
                    WriteInConsole("Unshare Folder " + ShareFolderErrorCodeToString(Convert.ToInt32(unshareOutParams)), LogType.info);
                }
            }

            // DELETE FOLDER AND FILE 

            WriteInConsole("Removing Share Folder " + remoteInfo.Address, LogType.info);
            //var Win32_Process_Class = new ManagementClass(theScope, new ManagementPath("Win32_Process"), new ObjectGetOptions());
            object[] cmdRMTemp = { @"cmd.exe /c rmdir /s /q " + REMOTEFOLDER };
            var rmResult = Win32_Process_Class.InvokeMethod("Create", cmdRMTemp);
            WriteInConsole("Remove Share Folder " + ErrorCodeToString(Convert.ToInt32(rmResult)), LogType.info);

            return 0;
        }

        private ManagementBaseObject SetShareParams(ManagementClass winShareClass, string filepath, string sharename)
        {
            var shareParams = winShareClass.GetMethodParameters("Create");
            shareParams["Path"] = filepath;
            shareParams["Name"] = sharename;
            shareParams["Type"] = 0;
            shareParams["Description"] = "Milestone HotFix";
            shareParams["MaximumAllowed"] = 10;
            shareParams["Password"] = null;

            NTAccount everyoneAccount = new NTAccount(null, "EVERYONE");
            SecurityIdentifier sid = (SecurityIdentifier)everyoneAccount.Translate(typeof(SecurityIdentifier));
            byte[] sidArray = new byte[sid.BinaryLength];
            sid.GetBinaryForm(sidArray, 0);

            ManagementObject everyone = new ManagementClass("Win32_Trustee");
            everyone["Domain"] = null;
            everyone["Name"] = "EVERYONE";
            everyone["SID"] = sidArray;

            ManagementObject dacl = new ManagementClass("Win32_Ace");
            dacl["AccessMask"] = 2032127;
            dacl["AceFlags"] = 3;
            dacl["AceType"] = 0;
            dacl["Trustee"] = everyone;

            ManagementObject securityDescriptor = new ManagementClass("Win32_SecurityDescriptor");
            securityDescriptor["ControlFlags"] = 4; //SE_DACL_PRESENT 
            securityDescriptor["DACL"] = new object[] { dacl };

            shareParams["Access"] = securityDescriptor;
            return shareParams;
        }

        private String ErrorCodeToString(int errorCode)
        {
            switch (errorCode)
            {
                case 0: return "Successful Completion";
                case 2: return "Access Denied";
                case 3: return "Insufficient Privilege";
                case 8: return "Unknown Failure";
                case 9: return "Path not found";
                case 21: return "Invalid parameter";
                default: return "ERROR";
            }
        }


        private String ShareFolderErrorCodeToString(int errorCode)
        {
            switch (errorCode)
            {
                case 0: return "Successful Completion";
                case 2: return "Access Denied";
                case 8: return "Unknown failure";
                case 9: return "Invalid name";
                case 10: return "Invalid level";
                case 21: return "Invalid parameter";
                case 22: return "Duplicate share";
                case 23: return "Redirected path";
                case 24: return "Unknown device or directory";
                case 25: return "Net name not found";
                default: return "ERROR";
            }
        }


        ConfigApiClient _configApiClient;
        string ms_Version;

        private void ButtonMSConnect_Click(object sender, EventArgs e)
        {
            ServerInfo ms_Info = new ServerInfo()
            {
                Address = textBoxMSAddress.Text,
                Domain = textBoxMSDomain.Text,
                UserName = textBoxMSUser.Text,
                Password = textBoxMSPass.Text
            };

            NetworkCredential nc = new NetworkCredential(ms_Info.UserName, ms_Info.Password, ms_Info.Domain);                               // Build credentials
            Uri uri = new Uri("http://" + ms_Info.Address);

            _configApiClient = new ConfigApiClient();
            Login(uri, nc, _configApiClient);

            ConfigurationItem managmentServer = _configApiClient.GetItem("/");
            string ms_Name = Array.Find(managmentServer.Properties, ele => ele.Key == "Name").Value;
            ms_Version = Array.Find(managmentServer.Properties, ele => ele.Key == "Version").Value;

            labelMSName.Text = ms_Name;
            labelMSVer.Text = ms_Version;

            /// ADD RECORDING SERVERS
            /// TODO: I DONT NEED THE API, but is easy.

            ConfigurationItem recordingServerFolder = _configApiClient.GetItem("/RecordingServerFolder");
            FillChildren(recordingServerFolder, _configApiClient);
            var recordingServerList = new List<KeyValuePair<String, String>>();
            foreach (ConfigurationItem recordingServer in recordingServerFolder.Children)
            {
                int n = dataGridView1.Rows.Add();

                dataGridView1.Rows[n].Cells["DisplayName"].Value = recordingServer.DisplayName;
                dataGridView1.Rows[n].Cells["Address"].Value = Array.Find(recordingServer.Properties, ele => ele.Key == "HostName").Value;

                dataGridView1.Rows[n].Cells["ServerType"].Value = HotFixType.RecordingServer;
                dataGridView1.Rows[n].Cells["Domain"].Value = ms_Info.Domain;
                dataGridView1.Rows[n].Cells["User"].Value = ms_Info.UserName;
                dataGridView1.Rows[n].Cells["Password"].Value = ms_Info.Password;
                dataGridView1.Rows[n].Cells["Selected"].Value = true;

            }

            /// TODO DO ADD ES-MOS
            /// 



            Logout(uri, nc, _configApiClient);

        }

        private void Logout(Uri uri, NetworkCredential nc, ConfigApiClient configApiClient)
        {
            WriteInConsole("Disconecting from : " + uri + ".", LogType.message);

            configApiClient.Close();
            configApiClient = null;

            VideoOS.Platform.SDK.Environment.Logout();
            VideoOS.Platform.SDK.Environment.RemoveAllServers();
        }

        private void buttonAllCredentials_Click(object sender, EventArgs e)
        {
            string _user = textBoxAllUser.Text;
            string _pass = textBoxAllPass.Text;
            string _domain = textBoxAllDomain.Text;

            for (int i = 0; i < dataGridView1.Rows.Count; i++)
            {
                dataGridView1.Rows[i].Cells["Domain"].Value = _domain;
                dataGridView1.Rows[i].Cells["User"].Value = _user;
                dataGridView1.Rows[i].Cells["Password"].Value = _pass;

            }
        }





        private void Login(Uri uri, NetworkCredential nc, ConfigApiClient _configApiClient)
        {

            // Clear the dataGrids 
            dataGridView1.Rows.Clear();
            dataGridViewHotFixList.Rows.Clear();

            VideoOS.Platform.SDK.Environment.Initialize();

            WriteInConsole("Connecting to: " + uri + ".", LogType.message);
            VideoOS.Platform.SDK.Environment.AddServer(false, uri, nc, true);                    // Add the server to the environment 
            try
            {
                VideoOS.Platform.SDK.Environment.Login(uri, IntegrationId, IntegrationName, Version, ManufacturerName);     // attempt to login 
            }
            catch (ServerNotFoundMIPException snfe)
            {
                WriteInConsole("Server not found: " + snfe.Message + ".", LogType.error);
            }
            catch (InvalidCredentialsMIPException ice)
            {
                WriteInConsole("Invalid credentials: " + ice.ToString() + ".", LogType.error);
            }

            catch (System.BadImageFormatException)
            {
                // Ignore this error. 

            }
            catch (Exception e)
            {
                WriteInConsole("Other error connecting to: " + e.ToString() + ".", LogType.error);
            }

            string _serverAddress = uri.ToString();                           // server URI
            int _serverPort = 80;                                             // Server port - TODO: Harcoded port 
            bool _corporate = true;                                           // c-code - TODO: Harcoded type

            _configApiClient.ServerAddress = _serverAddress;                  // set API Client
            _configApiClient.Serverport = _serverPort;
            _configApiClient.ServerType = _corporate
                                              ? ConfigApiClient.ServerTypeEnum.Corporate
                                              : ConfigApiClient.ServerTypeEnum.Arcus;

            try
            {
                _configApiClient.Initialize();                                    // Initialize API
            }
            catch (Exception ex)
            {
                WriteInConsole("API Error: " + ex, LogType.error);
            }

            if (_configApiClient.Connected)
            {
                WriteInConsole("Connection to : " + uri + " established.", LogType.message);
            }
            else
            {
                WriteInConsole("Connection to : " + uri + " failed.", LogType.error);
            }

        }



        /// Fill all the childs from a parent item 
        /// </summary>
        /// <param name="item">Parent ConfigurationItem</param>
        /// <param name="_configApiClient">Milesotone API</param>
        private void FillAllChilds(ConfigurationItem item, ConfigApiClient _configApiClient)
        {
            FillChildren(item, _configApiClient);                                                                   // Call aux method to get the children using the API
            foreach (var child in item.Children)                                                                    // For each child
            {
                FillAllChilds(child, _configApiClient);                                                             // Recurcive call
            }
        }

        /// <summary>
        /// Auxiliar methot to fill childs from a parent item 
        /// </summary>
        /// <param name="item">Parent ConfigurationItem</param>
        /// <param name="_configApiClient">Milesotone API</param>
        private void FillChildren(ConfigurationItem item, ConfigApiClient _configApiClient)
        {
            if (!item.ChildrenFilled)                                                                               //  If children was already filled continue 
            {
                item.Children = _configApiClient.GetChildItems(item.Path);                                          //  If not get the children with an API call
                item.ChildrenFilled = true;                                                                         //  Filled flag 
            }
            if (item.Children == null)                                                                              //  If children is null
                item.Children = new ConfigurationItem[0];                                                           //  Create a new object 
        }



        private String ResolveHostNametoIP(String host)
        {
            WriteInConsole("Resolving Name to IP: " + host, LogType.info);
            IPHostEntry hostEntry;
            hostEntry = Dns.GetHostEntry(host);
            string result = hostEntry.AddressList[0].ToString();
            WriteInConsole("Resolved Name to IP: " + host + " to " + result, LogType.message);
            return result;

        }
        private void WriteInConsole(string text, LogType type)
        {

            // if (type != LogType.debug)

            {

                textBox_Console.Invoke((MethodInvoker)delegate
                {
                    Color _color;
                    switch (type)
                    {
                        case LogType.debug:
                            _color = DEBUGCOLOR;
                            break;
                        case LogType.message:
                            _color = MESSAGECOLOR;
                            break;
                        case LogType.info:
                            _color = INFOCOLOR;
                            break;
                        case LogType.error:
                            _color = ERRORCOLOR;
                            break;
                        default:
                            _color = Color.White;
                            break;
                    }



                    textBox_Console.SelectionStart = textBox_Console.TextLength;
                    textBox_Console.SelectionLength = 0;

                    textBox_Console.SelectionColor = _color;
                    textBox_Console.AppendText(DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss") + ": " + text + Environment.NewLine);
                    textBox_Console.SelectionColor = textBox_Console.ForeColor;

                    textBox_Console.SelectionStart = textBox_Console.TextLength;
                    textBox_Console.ScrollToCaret();
                });
            }
        }

        enum LogType
        {
            debug,
            message,
            info,
            error,
        }

        private void RUN_Click(object sender, EventArgs e)
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if ((bool)row.Cells["Selected"].Value == true)
                {
                    ServerInfo remoteInfo = new ServerInfo()
                    {
                        Address = ResolveHostNametoIP(row.Cells["Address"].Value.ToString()),
                        Domain = row.Cells["Domain"].Value.ToString(),
                        UserName = row.Cells["User"].Value.ToString(),
                        Password = row.Cells["Password"].Value.ToString(),
                        ServerType = row.Cells["ServerType"].Value.ToString(),
                    };

                    foreach (DataGridViewRow _row in dataGridViewHotFixList.Rows)
                    {
                        if (_row.Cells["LocalLocation"].Value != null && _row.Cells["HotfixType"].Value.ToString() == remoteInfo.ServerType )
                        {
                            String file = _row.Cells["HotfixFile"].Value.ToString();
                            String filePath = _row.Cells["LocalLocation"].Value.ToString();
                            CallProcess(remoteInfo, filePath, file);
                        }
                    }
                }
            }
        }

        private void FindHotfixes_Click(object sender, EventArgs e2)
        {
            dataGridViewHotFixList.Rows.Clear();

            if (ms_Version == null)
            {
                WriteInConsole("Connect to a server first", LogType.error);
                return;
            }

            Hotfix hotfix;
            // using (StreamReader r = new StreamReader("hotfixList.json"))
            // string json = r.ReadToEnd();
            using (WebClient wc = new WebClient())
            {
                try
                {
                    var json = wc.DownloadString(HOTFIXLIST);
                    WriteInConsole("Got hotfix list from : " + HOTFIXLIST, LogType.info);
                    List<Hotfix> items = JsonConvert.DeserializeObject<List<Hotfix>>(json);
                    hotfix = items.Find(elem => ms_Version.Contains(elem.Version));
                    int n = dataGridViewHotFixList.Rows.Add();
                    dataGridViewHotFixList.Rows[n].Cells["HotfixType"].Value = HotFixType.RecordingServer;
                    dataGridViewHotFixList.Rows[n].Cells["HotfixUrl"].Value = hotfix.RS;
                    var files = GetFilesFromFolder(hotfix.RS);
                    String hotfixFile = files[0];
                    String hotfixFileReleaseNote = files[1];
                    dataGridViewHotFixList.Rows[n].Cells["HotfixFile"].Value = hotfixFile;

                }
                catch (Exception ex)
                {
                    WriteInConsole(ex.Message, LogType.error);
                }
            }
        }

        private String[] GetFilesFromFolder(string folder)
        {
            String[] result = new String[2];

            using (WebClient client = new WebClient())
            {
                string htmlCode = client.DownloadString(folder);
                WebBrowser webBrowser1 = new WebBrowser();
                webBrowser1.DocumentText = htmlCode;

                while (webBrowser1.ReadyState != WebBrowserReadyState.Complete)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(5);
                }

                if (webBrowser1.Document != null)
                {
                    HtmlElementCollection elems = webBrowser1.Document.GetElementsByTagName("a");
                    foreach (HtmlElement elem in elems)
                    {
                        String hrefStr = elem.GetAttribute("href");
                        if (hrefStr.Contains("Hotfix"))
                        {
                            if (hrefStr.Contains("ReleaseNotes"))
                            {
                                result[1] = hrefStr.Substring(hrefStr.LastIndexOf("/") + 1);
                            }
                            else
                                result[0] = hrefStr.Substring(hrefStr.LastIndexOf("/") + 1);
                        }
                    }
                }
            }
            return result;
        }

        enum HotFixType
        {
            RecordingServer,
            ManagementServer,
            EventServer,
            MovileServer
        }


        // HIDE PASSWORD ON DATAGRID
        private void dataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex == 5 && e.Value != null)
            {
                e.Value = new String('*', e.Value.ToString().Length);
            }
        }

        private void buttonDownload_Click(object sender, EventArgs e)
        {
            foreach (DataGridViewRow row in dataGridViewHotFixList.Rows)
            {

                //CHECK DOWNLOAD FOLDER 
                if (!Directory.Exists(LOCALFOLDER))
                {
                    Directory.CreateDirectory(LOCALFOLDER);
                }

                string remoteUri = row.Cells["HotfixUrl"].Value.ToString();
                string fileName = row.Cells["HotfixFile"].Value.ToString(), myStringWebResource = null;
                // Create a new WebClient instance.
                WebClient myWebClient = new WebClient();
                // Concatenate the domain with the Web resource filename.
                myStringWebResource = remoteUri + fileName;
                WriteInConsole("Downloading File " + fileName + " from  " + myStringWebResource + "...", LogType.message);
                // Download the Web resource and save it into the current filesystem folder.
                myWebClient.DownloadFile(myStringWebResource, LOCALFOLDER + "\\" + fileName);
                WriteInConsole("Successfully Downloaded File " + fileName + " from  " + myStringWebResource + "...", LogType.message);
                row.Cells["LocalLocation"].Value = LOCALFOLDER;
            }
        }
    }
}
