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
    public class DataStorageUtility : MonoBehaviour
    {
        private static Guid currentProcess = Guid.Empty;
        private static readonly Queue<QueuedTask> taskQueue = new();
        private static readonly Dictionary<Guid, TaskCompletionSource<Result>> downloadResults = new();

        private static Memory<byte> currentDataBuffer;
        private static int currentDataLength;

        public static async Task<DataStorageResult> GetPlayerDataStorageFile(DataStorageRequest request, bool clearCache = true)
        {
            Guid taskId = Guid.NewGuid();

            if (currentProcess == Guid.Empty) currentProcess = taskId;
            else await WaitForTurn(taskId);

            try
            {
                if (clearCache)
                {
                    Result res1 = await ClearPlayerDataStorageCache();
                    if (res1 != Result.Success)
                        TransportLogger.LogWarning($"Clearing Player Data Storage Cache failed with result 'Result.{res1}'.");
                }

                PDS.CopyFileMetadataByFilenameOptions metadataopt = new() { LocalUserId = EOSManager.LocalUserProductID, Filename = request.FileName };
                Result res2 = EOSManager.GetPlayerDataStorageInterface().CopyFileMetadataByFilename(ref metadataopt, out PDS.FileMetadata? metadata);
                if (res2 != Result.Success)
                {
                    TransportLogger.LogError($"Copying Player Data Storage metadata failed with result code 'Result.{res2}'.");
                    return new DataStorageResult(res2, request.FileName, null);
                }

                currentDataBuffer = new byte[metadata.Value.FileSizeBytes];
                currentDataLength = 0;

                TaskCompletionSource<Result> onDownloadFinished = new();
                downloadResults.Add(taskId, onDownloadFinished);

                PDS.ReadFileOptions readopt = new() { LocalUserId = EOSManager.LocalUserProductID, Filename = request.FileName, ReadChunkLengthBytes = 4096, ReadFileDataCallback = PlayerDataStorageReadFileCallback };
                EOSManager.GetPlayerDataStorageInterface().ReadFile(ref readopt, taskId, (ref PDS.ReadFileCallbackInfo cb) =>
                {
                    if (cb.ResultCode != Result.Success)
                    {
                        if (downloadResults.TryGetValue(taskId, out var tcs))
                        {
                            downloadResults.Remove(taskId);
                            onDownloadFinished.SetResult(cb.ResultCode);
                        }
                    }
                });

                await onDownloadFinished.Task;

                if (onDownloadFinished.Task.Result != Result.Success)
                {
                    TransportLogger.LogError($"Reading Player Data Storage file failed with result 'Result.{onDownloadFinished.Task.Result}'.");
                    return new DataStorageResult(onDownloadFinished.Task.Result, request.FileName, null);
                }

                byte[] resultData = currentDataBuffer[..currentDataLength].ToArray();

                TransportLogger.Log($"Player Data Storage GET: Successfully got file '{request.FileName}'. File Size: {Utils.PrettyBytes(resultData.LongLength)}");
                return new DataStorageResult(Result.Success, request.FileName, resultData);
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
                if (clearCache)
                {
                    Result res1 = await ClearPlayerDataStorageCache();
                    if (res1 != Result.Success)
                        TransportLogger.LogWarning($"Clearing Player Data Storage Cache failed with result 'Result.{res1}'.");
                }

                PDS.WriteFileOptions writeopt = new() { LocalUserId = EOSManager.LocalUserProductID, Filename = request.FileName, ChunkLengthBytes = 4096, WriteFileDataCallback = PlayerDataStorageWriteFileCallback };
                TaskCompletionSource<Result> writeTcs = new();

                currentDataBuffer = request.Data;
                currentDataLength = 0;

                EOSManager.GetPlayerDataStorageInterface().WriteFile(ref writeopt, taskId, (ref PDS.WriteFileCallbackInfo cb) =>
                {
                    writeTcs.SetResult(cb.ResultCode);
                });

                await writeTcs.Task;

                if (writeTcs.Task.Result != Result.Success)
                {
                    TransportLogger.LogError($"Writing Player Data Storage file failed with result 'Result.{writeTcs.Task.Result}'.");
                    return new DataStorageResult(writeTcs.Task.Result, request.FileName, null);
                }

                TransportLogger.Log($"Player Data Storage SET: Successfully wrote file '{request.FileName}'. File Size: {Utils.PrettyBytes(request.Data.LongLength)}");
                return new DataStorageResult(Result.Success, request.FileName, request.Data);
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
                if (clearCache)
                {
                    Result res1 = await ClearPlayerDataStorageCache();
                    if (res1 != Result.Success)
                        TransportLogger.LogWarning($"Clearing Player Data Storage Cache failed with result 'Result.{res1}'.");
                }

                TaskCompletionSource<Result> deleteTcs = new();

                PDS.DeleteFileOptions deleteopt = new() { LocalUserId = EOSManager.LocalUserProductID, Filename = request.FileName };
                EOSManager.GetPlayerDataStorageInterface().DeleteFile(ref deleteopt, null, (ref PDS.DeleteFileCallbackInfo cb) => { deleteTcs.SetResult(cb.ResultCode); });

                await deleteTcs.Task;

                if (deleteTcs.Task.Result != Result.Success)
                {
                    TransportLogger.LogError($"Failed to delete Player Data Storage file with result 'Result.{deleteTcs.Task.Result}'.");
                    return new DataStorageResult(deleteTcs.Task.Result, request.FileName, null);
                }

                TransportLogger.Log($"Player Data Storage DELETE: Successfully deleted file '{request.FileName}'.");
                return new DataStorageResult(deleteTcs.Task.Result, request.FileName, null);
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
                if (clearCache)
                {
                    Result res1 = await ClearTitleStorageCache();
                    if (res1 != Result.Success)
                        TransportLogger.LogWarning($"Clearing Title Storage Cache failed with result 'Result.{res1}'.");
                }

                TS.CopyFileMetadataByFilenameOptions metadataopt = new() { LocalUserId = EOSManager.LocalUserProductID, Filename = request.FileName };
                Result res2 = EOSManager.GetTitleStorageInterface().CopyFileMetadataByFilename(ref metadataopt, out TS.FileMetadata? metadata);

                if (res2 != Result.Success)
                {
                    TransportLogger.LogError($"Copying Title Storage metadata failed with result code 'Result.{res2}'.");
                    return new DataStorageResult(res2, request.FileName, null);
                }

                currentDataBuffer = new byte[metadata.Value.FileSizeBytes];
                currentDataLength = 0;

                TaskCompletionSource<Result> onDownloadFinished = new();
                downloadResults.Add(taskId, onDownloadFinished);

                TS.ReadFileOptions readopt = new() { LocalUserId = EOSManager.LocalUserProductID, Filename = request.FileName, ReadChunkLengthBytes = 4096, ReadFileDataCallback = TitleStorageReadFileCallback };
                EOSManager.GetTitleStorageInterface().ReadFile(ref readopt, taskId, (ref TS.ReadFileCallbackInfo cb) =>
                {
                    if (cb.ResultCode != Result.Success)
                    {
                        if (downloadResults.TryGetValue(taskId, out var tcs))
                        {
                            downloadResults.Remove(taskId);
                            onDownloadFinished.SetResult(cb.ResultCode);
                        }
                    }
                });

                await onDownloadFinished.Task;

                if (onDownloadFinished.Task.Result != Result.Success)
                {
                    TransportLogger.LogError($"Reading Title Storage file failed with result 'Result.{onDownloadFinished.Task.Result}'.");
                    return new DataStorageResult(onDownloadFinished.Task.Result, request.FileName, null);
                }

                byte[] resultData = currentDataBuffer[..currentDataLength].ToArray();

                TransportLogger.Log($"Title Storage GET: Successfully got file '{request.FileName}'. File Size: {Utils.PrettyBytes(resultData.LongLength)}");
                return new DataStorageResult(Result.Success, request.FileName, resultData);
            }
            finally { CompleteCurrentProcess(); }
        }

        /// <summary>
        /// Attempts to clear the EOS Player Data Storage cache.
        /// </summary>
        /// <returns>The result of the EOS method call.</returns>
        public static async Task<Result> ClearPlayerDataStorageCache()
        {
            TaskCompletionSource<Result> tcs = new();

            PDS.DeleteCacheOptions deleteopt = new() { LocalUserId = EOSManager.LocalUserProductID };
            EOSManager.GetPlayerDataStorageInterface().DeleteCache(ref deleteopt, null, (ref PDS.DeleteCacheCallbackInfo cb) => { tcs.SetResult(cb.ResultCode); });

            return await tcs.Task;
        }

        /// <summary>
        /// Attempts to clear the EOS Title Storage cache.
        /// </summary>
        /// <returns>The result of the EOS method call.</returns>
        public static async Task<Result> ClearTitleStorageCache()
        {
            TaskCompletionSource<Result> tcs = new();

            TS.DeleteCacheOptions deleteopt = new() { LocalUserId = EOSManager.LocalUserProductID };
            EOSManager.GetTitleStorageInterface().DeleteCache(ref deleteopt, null, (ref TS.DeleteCacheCallbackInfo cb) => { tcs.SetResult(cb.ResultCode); });

            return await tcs.Task;
        }

        private static async Task WaitForTurn(Guid taskId)
        {
            if (currentProcess == taskId) return;
            TaskCompletionSource<bool> readySignal = new();

            taskQueue.Enqueue(new QueuedTask() { taskId = taskId, readySignal = readySignal });
            await readySignal.Task;
        }

        private static PDS.WriteResult PlayerDataStorageWriteFileCallback(ref PDS.WriteFileDataCallbackInfo cb, out ArraySegment<byte> outDataBuffer)
        {
            if (cb.ClientData == null)
            {
                outDataBuffer = null;
                return PDS.WriteResult.CancelRequest;
            }

            int totalLength = currentDataBuffer.Length;
            int remaining = totalLength - currentDataLength;

            if (remaining <= 0)
            {
                outDataBuffer = ArraySegment<byte>.Empty;
                return PDS.WriteResult.CompleteRequest;
            }

            int chunkSize = Math.Min((int)cb.DataBufferLengthBytes, remaining);
            outDataBuffer = new ArraySegment<byte>(currentDataBuffer.Slice(currentDataLength, chunkSize).ToArray());
            currentDataLength += chunkSize;

            return currentDataLength >= totalLength ? PDS.WriteResult.CompleteRequest : PDS.WriteResult.ContinueWriting;
        }

        private static PDS.ReadResult PlayerDataStorageReadFileCallback(ref PDS.ReadFileDataCallbackInfo cb)
        {
            if (cb.ClientData == null) return PDS.ReadResult.CancelRequest;

            Guid taskId = (Guid)cb.ClientData;
            if (taskId != currentProcess || !downloadResults.ContainsKey(taskId))
                return PDS.ReadResult.CancelRequest;

            try
            {
                cb.DataChunk.AsSpan().CopyTo(currentDataBuffer.Span[currentDataLength..]);
                currentDataLength += cb.DataChunk.Count;

                if (cb.IsLastChunk)
                {
                    downloadResults[taskId].SetResult(Result.Success);
                    downloadResults.Remove(taskId);
                }
                return PDS.ReadResult.ContinueReading;
            }
            catch (Exception ex)
            {
                if (downloadResults.TryGetValue(taskId, out var tcs))
                {
                    tcs.SetException(ex);
                    downloadResults.Remove(taskId);
                }
                return PDS.ReadResult.FailRequest;
            }
        }

        private static TS.ReadResult TitleStorageReadFileCallback(ref TS.ReadFileDataCallbackInfo cb)
        {
            if (cb.ClientData == null) return TS.ReadResult.RrCancelRequest;

            Guid taskId = (Guid)cb.ClientData;
            if (taskId != currentProcess || !downloadResults.ContainsKey(taskId))
                return TS.ReadResult.RrCancelRequest;

            try
            {
                cb.DataChunk.AsSpan().CopyTo(currentDataBuffer.Span[currentDataLength..]);
                currentDataLength += cb.DataChunk.Count;

                if (cb.IsLastChunk)
                {
                    downloadResults[taskId].SetResult(Result.Success);
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
            currentDataBuffer = default;
            currentDataLength = 0;

            if (taskQueue.Count > 0)
            {
                QueuedTask nextTask = taskQueue.Dequeue();
                currentProcess = nextTask.taskId;
                nextTask.readySignal.SetResult(true);
            }
            else currentProcess = Guid.Empty;
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
            FileName = fileName;
            Data = System.Text.Encoding.UTF8.GetBytes(data);
        }

        /// <summary>
        /// Creates a new request for data storage.
        /// </summary>
        /// <remarks>This overload should ONLY be used along with the Set(x)File methods.</remarks>
        /// <param name="fileName">The name of the file to add/update.</param>
        /// <param name="data">The data of the file to add/update, as a byte array. Use the string overload to send as a UTF-8 string.</param>
        public DataStorageRequest(string fileName, byte[] data)
        {
            FileName = fileName;
            Data = data;
        }

        /// <summary>
        /// Creates a new request for data storage.
        /// </summary>
        /// <remarks>This overload should ONLY be used along with the Get(x)File and Delete(x)File methods.</remarks>
        /// <param name="fileName">The name of the file to get.</param>
        public DataStorageRequest(string fileName)
        {
            FileName = fileName;
            Data = null;
        }

        public string FileName;
        public byte[] Data;
    }

    public struct DataStorageResult
    {
        internal DataStorageResult(Result result, string fileName, byte[] data)
        {
            Success = result == Result.Success;
            Result = result;
            FileName = fileName;
            Data = data;
        }

        public readonly bool Success;
        public readonly Result Result;
        public readonly string FileName;
        public readonly byte[] Data;

        /// <summary>
        /// Turns <see cref="Data"/> into a UTF-8 string.
        /// </summary>
        /// <returns>The UTF-8 string of <see cref="Data"/> if it's not null, <see cref="string.Empty"/> otherwise.</returns>
        public readonly string DataToUtf8String()
        {
            if (Data != null) return System.Text.Encoding.UTF8.GetString(Data);
            else return string.Empty;
        }
    }
}
