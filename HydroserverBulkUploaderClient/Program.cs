using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using System.Net.Http.Headers;

using HydroserverBulkUploaderClient.Utilities;

namespace HydroserverBulkUploaderClient
{
    class Program
    {
        //Private members...
        private static CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private static ConcurrentDictionary<Task, CancellationTokenSource> _concurrentDictionaryTasks = new ConcurrentDictionary<Task, CancellationTokenSource>();

        //Not ORable!!
        private enum _eCommandIndex
        {
            [Description("cancelTask")]
            cancelTask = 0x0,
            [Description("listCommands")]
            listCommands = 0x1,
            [Description("quitApplication")]
            quitApplication = 0x2,
            [Description("uploadFile")]
            uploadFile = 0x3
        };

        private static Dictionary<string, string> _dictCommands = new Dictionary<string, string> { { EnumUtil.GetDescription(_eCommandIndex.cancelTask), "Cancel current task" },
                                                                                                   { EnumUtil.GetDescription(_eCommandIndex.listCommands), "List commands"},
                                                                                                   { EnumUtil.GetDescription(_eCommandIndex.quitApplication), "Quit application" },
                                                                                                   { String.Format("{0} {1}", 
                                                                                                                    EnumUtil.GetDescription(_eCommandIndex.uploadFile), 
                                                                                                                    "<file path and name>"), "Upload one file" } };

        private const string _commandPrefix = "--";

        //Private methods...

        //List commands...
        private static void listCommands()
        {
            Console.WriteLine("");
            Console.WriteLine("Supported commands: ");
            Console.WriteLine("");
            foreach (var kvp in _dictCommands)
            {
                Console.WriteLine(String.Format("\t{0}{1} - {2}", _commandPrefix, kvp.Key, kvp.Value));
            }
        }

        //Parse commands...
        private static bool parseCommands(string input, ref string command, ref string parameter)
        {
            bool bResult = false;    //Assume failure...

            //Validate/initialize input parameters...
            command = String.Empty;
            parameter = String.Empty;

            if (! String.IsNullOrWhiteSpace(input))
            {
                //Input parameters valid - check input for supported command...
                foreach (var kvp in _dictCommands)
                {
                    string key = kvp.Key;
                    int index = key.IndexOf(' ');
                    string rootCommand = key.Substring(0, -1 != index ? index : key.Length);
                    string tempCommand = _commandPrefix + rootCommand;
                    if (input.Contains(tempCommand, StringComparison.CurrentCultureIgnoreCase))
                    {
                        command = rootCommand;
                        bResult = true; //Success

                        if ("uploadFile" == rootCommand)
                        {
                            //Upload file command - retrieve file path and name parameter from input...
                            if ((-1 != index) && ((_commandPrefix.Length + index) < input.Length))
                            {
                                string tempPath = input.Substring((_commandPrefix.Length + index + 1));

                                //Check for valid file path and name...
                                try
                                {
                                    parameter = Path.GetFullPath(tempPath);
                                    bResult = File.Exists(parameter);
                                }
                                catch(Exception)
                                {
                                    //Invalid file path and name - reset result
                                    bResult = false;
                                }
                            }
                            else
                            {
                                //No file path and name entered - set result
                                bResult = false;
                            }
                        }

                        break;
                    }
                }
            }

            //Processing complete - return
            return bResult;
        }

        private static int UploadFileTask(string filePathAndName, CancellationToken cancellationToken)
        {
            //Validate/initialize input parameters...
            int bytesRead = 0;
            if (String.IsNullOrWhiteSpace(filePathAndName) || null == cancellationToken)
            {
                //Invalid parameter(s) - return early
                return bytesRead;
            }

            //Open file for reading...
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                //Open file stream...
                using (FileStream fs = new FileStream(filePathAndName, FileMode.Open, FileAccess.Read))
                {
                    if (fs.CanSeek)
                    {
                        //Seek to beginning of stream, if indicated...
                        fs.Seek(0, SeekOrigin.Begin);
                    }

                    //Retrieve file name...
                    FileInfo fi = new FileInfo(filePathAndName);
                    var fileName = fi.Name;

                    //Determine file length, read buffer size...
                    long fLength = fs.Length;

                    int maxChunkSize = int.Parse(HydroshareBulkUploaderClient.MaxFileChunkBytes);
                    int bytesToRead = (fLength < maxChunkSize) ? (int)fLength : maxChunkSize;

                    //Set indicator...
                    bool bChunked = bytesToRead == maxChunkSize;

                    //Allocate read buffer...
                    byte[] readBuffer = new byte[bytesToRead];

                    while (bytesRead < fLength)
                    {
                        int result = fs.Read(readBuffer, 0, bytesToRead);
                        bytesRead += result;

                        if (0 < result)
                        {
                            //File contents read - prepare HTTP headers and POST...
                            using (HttpClient httpClient = new HttpClient())
                            {
                                //Base address...
                                //httpClient.BaseAddress = new Uri(HydroshareBulkUploaderClient.BulkUploadApiBaseAddress);
                                //httpClient.BaseAddress = new Uri(HydroshareBulkUploaderClient.azure_BulkUploadBaseAddress);
                                httpClient.BaseAddress = new Uri(HydroshareBulkUploaderClient.HydroServerBulkUploadApiBaseAddress);

                                //User agent...
                                httpClient.DefaultRequestHeaders.Add("User-Agent", "CUAHSI Bulk Uploader Client");
                                //Connection...
                                httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

                                //For now - add a 'dummy' upload Id...
                                httpClient.DefaultRequestHeaders.Add("X-Api-BulkUpload-UploadId", "abcd-1234");

                                //Add validation qualifier...
                                httpClient.DefaultRequestHeaders.Add("X-Api-BulkUpload-ValidationQualifier", "bulk-upload");

                                var strBoundary = String.Format("----------Boundary{0:N}", Guid.NewGuid()); //Format GUID as 32 digits...
                                using (var content = new MultipartFormDataContent(strBoundary))
                                {
                                    //Byte content...
                                    var byteContent = new ByteArrayContent(readBuffer, 0, result);
                                    //Content-Length...
                                    byteContent.Headers.ContentLength = result;
                                    //Content-Type...
                                    byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ms-excel");

                                    if (bChunked)
                                    {
                                        //Content-Range...
                                        byteContent.Headers.ContentRange = new ContentRangeHeaderValue((bytesRead - result), (bytesRead - 1), fLength);
                                    }

                                    //Content-Disposition...
                                    content.Add(byteContent, "files", fileName);

                                    //Post content...
                                    //HttpResponseMessage response = httpClient.PostAsync("Hydroserver/BulkUploadApi/", content, cancellationToken).Result;
                                    HttpResponseMessage response = httpClient.PostAsync(HydroshareBulkUploaderClient.HydroServerBulkUploadApiPost, 
                                                                                        content, 
                                                                                        cancellationToken).Result;
                                    if (response.IsSuccessStatusCode)
                                    {
                                        Console.WriteLine("");
                                        Console.WriteLine("Successful post!!");
                                    }
                                    else
                                    {
                                        Console.WriteLine("");
                                        Console.WriteLine(String.Format("Post Error: {0} - {1}", response.StatusCode.ToString(), response.ReasonPhrase));
                                    }
                                }
                            }
                        }

                        //Recalculate remaining bytes to read
                        long unreadBytes = fLength - bytesRead;
                        bytesToRead = (unreadBytes < maxChunkSize) ? (int)unreadBytes : maxChunkSize; ;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ex.GetType().Equals(typeof(OperationCanceledException)))
                {
                    //Exception other than cancellation, rethrow...
                    throw ex;
                }
            }

            //Processing complete - return
            return bytesRead;

        }

        //Start file upload
        private static Task StartFileUpload(string filePathAndName, CancellationToken cancellationToken)
        {
            Task result = null;

            //Validate/initialize input parameters...
            if (!String.IsNullOrWhiteSpace(filePathAndName) && (null != cancellationToken))
            {
                Task<int> myTask = Task<int>.Factory.StartNew(() => UploadFileTask(filePathAndName, cancellationToken));
                result = myTask;
            }

            return result;
        }

        //Submit POST request...
        private static async Task<HttpResponseMessage> submitPostAsync(CancellationToken cancellationToken)
        {
            HttpResponseMessage httpResponseMsg = null;

            //Validate/initialize input parameters...
            if (null == cancellationToken)
            {
                throw new ArgumentNullException("cancellationToken");
            }

            //Input parameters valid - prepare to submit post...
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    //httpClient.BaseAddress = new Uri(HydroshareBulkUploaderClient.BulkUploadApiBaseAddress);
                    httpClient.BaseAddress = new Uri(HydroshareBulkUploaderClient.BulkUploadApiBaseAddress);

                    HttpContent httpContent = new MultipartFormDataContent("myBoundaryString");

                    //httpResponseMsg = await httpClient.PostAsync("Hydroserver/BulkUploadApi/", httpContent, cancellationToken);
                    httpResponseMsg = await httpClient.PostAsync("api/bulkupload/", httpContent, cancellationToken);

                    if (httpResponseMsg.IsSuccessStatusCode)
                    {
                        Console.WriteLine("");
                        Console.WriteLine("Successful post!!");
                    }
                    else
                    {
                        Console.WriteLine("");
                        Console.WriteLine(String.Format("Post Error: {0} - {1}", httpResponseMsg.StatusCode.ToString(), httpResponseMsg.ReasonPhrase));
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ex.GetType().Equals(typeof(OperationCanceledException)))
                {
                    //Exception other than cancellation, rethrow...
                    throw ex;
                }
            }

            //Processing complete - return
            return httpResponseMsg;
        }

        static void Main(string[] args)
        {
            listCommands();
            bool bContinue = true;

            while (bContinue)
            {
                //Read user input...
                string input = Console.ReadLine().Trim();
                if (String.IsNullOrEmpty(input))
                {
                    continue;   //No command(s) entered...
                }

                //Parse input command string...
                string command = String.Empty;
                string parameter = String.Empty;

                bool bResult = parseCommands(input, ref command, ref parameter);
                if (!bResult)
                {
                    //Invalid command...
                    Console.WriteLine("");
                    Console.WriteLine(String.Format("Invalid command entered: '{0}'", input));
                    continue;
                }
                else
                {
                    //Valid command...
                    Console.WriteLine("");
                    if (String.IsNullOrEmpty(parameter))
                    {
                        Console.WriteLine(String.Format("Valid command entered: '{0}'", command));
                    }
                    else
                    {
                        Console.WriteLine(String.Format("Valid command entered: '{0} - {1}'", command, parameter));
                    }

                    //Perform requested command...
                    if (EnumUtil.GetDescription(_eCommandIndex.cancelTask) == command)
                    {
                        //Cancel task...
                        Console.WriteLine("");
                        Console.WriteLine("Cancelling current task...");
                        _cancellationTokenSource.Cancel();
                    }
                    else if (EnumUtil.GetDescription(_eCommandIndex.listCommands) == command)
                    {
                        //List commands...
                        listCommands();
                    }
                    else if (EnumUtil.GetDescription(_eCommandIndex.quitApplication) == command)
                    {
                        //Quit application...
                        Console.WriteLine("");
                        Console.WriteLine("Quitting application...");
                        bContinue = false;
                    }
                    else if (EnumUtil.GetDescription(_eCommandIndex.uploadFile) == command)
                    {
                        //Upload file...
                        Console.WriteLine("");
                        Console.WriteLine(String.Format("Upload file: {0}", parameter));

                        CancellationTokenSource cTS = new CancellationTokenSource();

                        Task fileTask = StartFileUpload(parameter, cTS.Token);
                        if (null != fileTask)
                        {
                            _concurrentDictionaryTasks.TryAdd(fileTask, cTS);
                        }
                    }
                }
            }

            //Await running tasks, if any
            Console.WriteLine("");
            foreach (var kvp in _concurrentDictionaryTasks)
            {
                var task = kvp.Key;
                if ((!task.IsCanceled) && (!task.IsCompleted))
                {
                    Console.WriteLine(String.Format("Waiting for task: {0}", task.Id));
                    task.Wait();
                }
            }

            //Exit environment - return
            Environment.Exit(0);
            return;
        }
    }
}

