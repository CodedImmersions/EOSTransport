using Epic.OnlineServices;
using Mirror;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using PDS = Epic.OnlineServices.PlayerDataStorage;
using TS = Epic.OnlineServices.TitleStorage;

namespace EpicTransport
{
    //TODO: add logged in checks
    //TODO: add delete file for pds
    public class DataStorageUtility : MonoBehaviour
    {
        private static Guid currentProcess = Guid.Empty;
        private static Queue<QueuedTask> taskQueue = new Queue<QueuedTask>();
        private static Dictionary<Guid, TaskCompletionSource<bool>> downloadResults = new Dictionary<Guid, TaskCompletionSource<bool>>();

        private static Memory<byte> currentDataBuffer;
        private static int currentDataLength;

        public static async Task<DataStorageResult> GetPlayerDataStorageFile(DataStorageRequest request, bool clearCache = true)
        {
            Guid taskId = Guid.NewGuid();

            if (currentProcess == Guid.Empty) currentProcess = taskId;
            else await WaitForTurn(taskId);

            try
            {
                if (clearCache) await ClearPlayerDataStorageCache();

                return new DataStorageResult(); //TODO:
            }
            finally { CompleteCurrentProcess(); }
        }

        public static async Task<DataStorageResult> SetPlayerDataStorageFile(DataStorageRequest request, bool clearCache = true)
        {
            Guid taskId = Guid.NewGuid();

            if (currentProcess == Guid.Empty) currentProcess = taskId;
            else await WaitForTurn(taskId);

            try
            {
                if (clearCache) await ClearPlayerDataStorageCache();

                return new DataStorageResult(); //TODO:
            }
            finally { CompleteCurrentProcess(); }
        }

        public static async Task<DataStorageResult> DeletePlayerDataStorageFile(DataStorageRequest request, bool clearCache = true)
        {
            Guid taskId = Guid.NewGuid();

            if (currentProcess == Guid.Empty) currentProcess = taskId;
            else await WaitForTurn(taskId);

            try
            {
                if (clearCache) await ClearPlayerDataStorageCache();

                return new DataStorageResult(); //TODO:
            }
            finally { CompleteCurrentProcess(); }
        }

        public static async Task<DataStorageResult> GetTitleStorageFile(DataStorageRequest request, bool clearCache = true)
        {
            Guid taskId = Guid.NewGuid();

            if (currentProcess == Guid.Empty) currentProcess = taskId;
            else await WaitForTurn(taskId);

            try
            {
                if (clearCache) await ClearTitleStorageCache();
                TaskCompletionSource<Result> queryTcs = new TaskCompletionSource<Result>();

                TS.QueryFileOptions queryopt = new() { LocalUserId = EOSManager.LocalUserProductID, Filename = request.fileName };
                EOSManager.GetTitleStorageInterface().QueryFile(ref queryopt, null, (ref TS.QueryFileCallbackInfo cb) =>
                {
                    queryTcs.SetResult(cb.ResultCode);
                });
                await queryTcs.Task;

                if (queryTcs.Task.Result != Result.Success)
                    throw new EOSSDKException(queryTcs.Task.Result, "Failed to query Title Storage file!");

                TS.CopyFileMetadataByFilenameOptions metadataopt = new() { LocalUserId = EOSManager.LocalUserProductID, Filename = request.fileName };
                Result res1 = EOSManager.GetTitleStorageInterface().CopyFileMetadataByFilename(ref metadataopt, out TS.FileMetadata? metadata);

                if (res1 != Result.Success)
                    throw new EOSSDKException(res1, "Failed to copy Title Storage file metadata!");

                currentDataBuffer = new byte[metadata.Value.FileSizeBytes];
                currentDataLength = 0;

                TaskCompletionSource<bool> onDownloadFinished = new TaskCompletionSource<bool>();
                downloadResults.Add(taskId, onDownloadFinished);

                TS.ReadFileOptions readopt = new() { LocalUserId = EOSManager.LocalUserProductID, Filename = request.fileName, ReadChunkLengthBytes = 4096, ReadFileDataCallback = TitleStorageReadFileCallback };
                EOSManager.GetTitleStorageInterface().ReadFile(ref readopt, taskId, (ref TS.ReadFileCallbackInfo cb) =>
                {
                    if (cb.ResultCode != Result.Success)
                    {
                        if (downloadResults.TryGetValue(taskId, out var tcs))
                        {
                            tcs.SetException(new EOSSDKException(cb.ResultCode, "Failed to read Title Storage file!"));
                            downloadResults.Remove(taskId);
                        }
                    }
                });

                await onDownloadFinished.Task;

                downloadResults.Remove(taskId);
                byte[] resultData = currentDataBuffer.Slice(0, currentDataLength).ToArray();

                TransportLogger.Log($"Title Storage GET: Successfully got file '{request.fileName}'. File Size: {Utils.PrettyBytes(resultData.LongLength)}");
                return new DataStorageResult(Result.Success, request.fileName, resultData);
            }
            finally { CompleteCurrentProcess(); }
        }

        /// <summary>
        /// Attempts to clear the EOS Player Data Storage cache.
        /// </summary>
        /// <returns>The result of the EOS method call.</returns>
        /// <exception cref="EOSSDKException">Thrown when EOS returns any other results than Success or AlreadyPending.</exception>
        public static async Task<Result> ClearPlayerDataStorageCache()
        {
            TaskCompletionSource<Result> tcs = new TaskCompletionSource<Result>();

            PDS.DeleteCacheOptions deleteopt = new() { LocalUserId = EOSManager.LocalUserProductID };
            EOSManager.GetPlayerDataStorageInterface().DeleteCache(ref deleteopt, null, (ref PDS.DeleteCacheCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success && cb.ResultCode != Result.AlreadyPending) tcs.SetException(new EOSSDKException(cb.ResultCode, "Failed to clear Player Data Storage cache!"));
                else tcs.SetResult(cb.ResultCode);
            });

            return await tcs.Task;
        }

        /// <summary>
        /// Attempts to clear the EOS Title Storage cache.
        /// </summary>
        /// <returns>The result of the EOS method call.</returns>
        /// <exception cref="EOSSDKException">Thrown when EOS returns any other results than Success or AlreadyPending.</exception>
        public static async Task<Result> ClearTitleStorageCache()
        {
            TaskCompletionSource<Result> tcs = new TaskCompletionSource<Result>();

            TS.DeleteCacheOptions deleteopt = new() { LocalUserId = EOSManager.LocalUserProductID };
            EOSManager.GetTitleStorageInterface().DeleteCache(ref deleteopt, null, (ref TS.DeleteCacheCallbackInfo cb) =>
            {
                if (cb.ResultCode != Result.Success && cb.ResultCode != Result.AlreadyPending) tcs.SetException(new EOSSDKException(cb.ResultCode, "Failed to clear Title Storage cache!"));
                else tcs.SetResult(cb.ResultCode);
            });

            return await tcs.Task;
        }

        private static async Task WaitForTurn(Guid taskId)
        {
            if (currentProcess == taskId) return;
            TaskCompletionSource<bool> readySignal = new TaskCompletionSource<bool>();

            taskQueue.Enqueue(new QueuedTask() { taskId = taskId, readySignal = readySignal });
            await readySignal.Task;
        }

        private static TS.ReadResult TitleStorageReadFileCallback(ref TS.ReadFileDataCallbackInfo cb)
        {
            if (cb.ClientData == null) return TS.ReadResult.RrCancelRequest;

            Guid taskId = (Guid)cb.ClientData;
            if (taskId != currentProcess || !downloadResults.ContainsKey(taskId))
                return TS.ReadResult.RrCancelRequest;

            try
            {
                cb.DataChunk.AsSpan().CopyTo(currentDataBuffer.Span.Slice(currentDataLength));
                currentDataLength += cb.DataChunk.Count;

                if (cb.IsLastChunk)
                {
                    downloadResults[taskId].SetResult(true);
                    downloadResults.Remove(taskId);
                }
                return TS.ReadResult.RrContinueReading;
            }
            catch (Exception ex)
            {
                if (downloadResults.TryGetValue(taskId, out var tcs))
                {
                    tcs.SetException(ex);
                    downloadResults.Remove(taskId);
                }
                return TS.ReadResult.RrFailRequest;
            }
        }

        private static void CompleteCurrentProcess()
        {
            if (taskQueue.Count > 0)
            {
                QueuedTask nextTask = taskQueue.Dequeue();
                currentProcess = nextTask.taskId;
                nextTask.readySignal.SetResult(true);
            }
            else currentProcess = Guid.Empty;

            currentDataBuffer = null;
            currentDataLength = -1;
        }

        private class QueuedTask
        {
            public Guid taskId;
            public TaskCompletionSource<bool> readySignal;
        }
    }

    public struct DataStorageRequest
    {
        /// <summary>
        /// Creates a new request for data storage.
        /// </summary>
        /// <remarks>This overload should ONLY be used along with the Set(x)File methods.</remarks>
        /// <param name="fileName">The name of the file to add/update.</param>
        /// <param name="data">The data of the file to add/update, as a string. Use the byte[] overload to send raw data.</param>
        public DataStorageRequest(string fileName, string data)
        {
            this.fileName = fileName;
            this.data = System.Text.Encoding.UTF8.GetBytes(data);
        }

        /// <summary>
        /// Creates a new request for data storage.
        /// </summary>
        /// <remarks>This overload should ONLY be used along with the Set(x)File methods.</remarks>
        /// <param name="fileName">The name of the file to add/update.</param>
        /// <param name="data">The data of the file to add/update, as a byte array. Use the string overload to send as a UTF-8 string.</param>
        public DataStorageRequest(string fileName, byte[] data)
        {
            this.fileName = fileName;
            this.data = data;
        }

        /// <summary>
        /// Creates a new request for data storage.
        /// </summary>
        /// <remarks>This overload should ONLY be used along with the Get(x)File and Delete(x)File methods.</remarks>
        /// <param name="fileName">The name of the file to get.</param>
        public DataStorageRequest(string fileName)
        {
            this.fileName = fileName;
            this.data = null;
        }

        public string fileName;
        public byte[] data;
    }

    public struct DataStorageResult
    {
        internal DataStorageResult(Result result, string fileName, byte[] data)
        {
            this.result = result;
            this.fileName = fileName;
            this.data = data;
        }

        public Result result;
        public string fileName;
        public byte[] data;

        public string DataToUtf8String()
        {
            return System.Text.Encoding.UTF8.GetString(data);
        }
    }
}
