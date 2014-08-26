﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using SODA.Utilities;

namespace SODA
{
    /// <summary>
    /// A class for interacting with Socrata Data Portals using the Socrata Open Data API.
    /// </summary>
    public class SodaClient
    {
        /// <summary>
        /// The url to the Socrata Open Data Portal this client will target.
        /// </summary>
        public readonly string Host;

        /// <summary>
        /// The Socrata application token that this client will use for all requests.
        /// </summary>
        /// <remarks>
        /// Since SodaClient uses Basic Authentication, the application token is only used as a means to reduce API throttling on the part of Socrata.
        /// See http://dev.socrata.com/docs/app-tokens.html for more information.
        /// </remarks>
        public readonly string AppToken;

        /// <summary>
        /// The user account that this client will use for Authentication during each request.
        /// </summary>
        /// <remarks>
        /// Authentication is only necessary when accessing datasets that have been marked as private or when making write requests (PUT, POST, and DELETE).
        /// See http://dev.socrata.com/docs/authentication.html for more information.
        /// </remarks>
        public readonly string Username;
        
        //not publicly readable, can only be set in a constructor
        private readonly string password;

        /// <summary>
        /// Helper method for getting the response string from an instance of a WebException.
        /// </summary>
        /// <param name="webException">The WebException whose response string will be read.</param>
        /// <returns>The response string if it exists, otherwise the Message property of the WebException.</returns>
        internal static string unwrapExceptionMessage(WebException webException)
        {
            string message = String.Empty;

            if (webException != null)
            {
                message = webException.Message;

                if (webException.Response != null)
                {
                    using (var streamReader = new StreamReader(webException.Response.GetResponseStream()))
                    {
                        message = streamReader.ReadToEnd();
                    }
                }
            }

            return message;
        }

        /// <summary>
        /// Send an HTTP GET request to the specified URI and intepret the result as TResult.
        /// </summary>
        /// <typeparam name="TResult">The .NET class to use for response deserialization.</typeparam>
        /// <param name="uri">A uniform resource identifier that is the target of this GET request.</param>
        /// <param name="dataFormat">One of the data-interchange formats that Socrata supports. The default is JSON.</param>
        /// <returns>The HTTP response, deserialized into an object of type <typeparamref name="TResult"/>.</returns>
        internal TResult read<TResult>(Uri uri, SodaDataFormat dataFormat = SodaDataFormat.JSON)
            where TResult : class
        {
            var request = new SodaRequest(uri, "GET", AppToken, Username, password, dataFormat);

            return request.ParseResponse<TResult>();
        }

        /// <summary>
        /// Send an HTTP request of the specified method and interpret the result.
        /// </summary>
        /// <typeparam name="TPayload">The .NET class that represents the request payload.</typeparam>
        /// <typeparam name="TResult">The .NET class to use for response deserialization.</typeparam>
        /// <param name="uri">A uniform resource identifier that is the target of this GET request.</param>
        /// <param name="method">One of POST, PUT, or DELETE</param>
        /// <param name="payload">An object graph to serialize and send with the request.</param>
        /// <returns>The HTTP response, deserialized into an object of type <typeparamref name="TResult"/>.</returns>
        internal TResult write<TPayload, TResult>(Uri uri, string method, TPayload payload)
            where TPayload : class
            where TResult : class
        {
            var request = new SodaRequest(uri, method, AppToken, Username, password, SodaDataFormat.JSON, payload.ToJsonString());

            return request.ParseResponse<TResult>();
        }

        /// <summary>
        /// Initialize a new SodaClient for the specified Socrata host, using the specified application token and the specified Authentication credentials.
        /// </summary>
        /// <param name="host">The Socrata Open Data Portal that this client will target.</param>
        /// <param name="appToken">The Socrata application token that this client will use for all requests.</param>
        /// <param name="username">The user account that this client will use for Authentication during each request.</param>
        /// <param name="password">The password for the specified <paramref name="username"/> that this client will use for Authentication during each request.</param>
        public SodaClient(string host, string appToken, string username, string password)
        {
            if (String.IsNullOrEmpty(host))
                throw new ArgumentException("host", "A host is required");

            if (String.IsNullOrEmpty(appToken))
                throw new ArgumentException("appToken", "An app token is required");
            
            Host = SodaUri.enforceHttps(host);
            AppToken = appToken;
            Username = username;
            this.password = password;
        }
        
        /// <summary>
        /// Initialize a new (anonymous) SodaClient for the specified Socrata host, using the specified application token.
        /// </summary>
        /// <param name="host">The Socrata Open Data Portal that this client will target.</param>
        /// <param name="appToken">The Socrata application token that this client will use for all requests.</param>
        public SodaClient(string host, string appToken)
            : this(host, appToken, null, null)
        {
        }
        
        /// <summary>
        /// Get a ResourceMetadata object using the specified resource identifier.
        /// </summary>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>
        /// A ResourceMetadata object for the specified resource identifier.
        /// </returns>
        public ResourceMetadata GetMetadata(string resourceId)
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            var uri = SodaUri.ForMetadata(Host, resourceId);

            var metadata = read<ResourceMetadata>(uri);
            metadata.Client = this;

            return metadata;
        }

        /// <summary>
        /// Get a collection of ResourceMetadata objects on the specified page.
        /// </summary>
        /// <param name="page">The 1-indexed page of the Metadata Catalog on this client's Socrata host.</param>
        /// <returns>A collection of ResourceMetadata objects from the specified page of this client's Socrata host.</returns>
        public IEnumerable<ResourceMetadata> GetMetadataPage(int page)
        {
            if (page <= 0)
                throw new ArgumentOutOfRangeException("page", "Resouce metadata catalogs begin on page 1.");
            
            var catalogUri = SodaUri.ForMetadataList(Host, page);

            IEnumerable<dynamic> rawDataList = read<IEnumerable<dynamic>>(catalogUri).ToArray();

            foreach (var rawData in rawDataList)
            {
                var metadata = GetMetadata((string)rawData.id);

                yield return metadata;
            }
        }

        /// <summary>
        /// Get a Resource object using the specified resource identifier.
        /// </summary>
        /// <typeparam name="TRow">The .NET class that represents the type of the underlying row in the Resource.</typeparam>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A Resource object with an underlying row set of type <typeparamref name="TRow"/>.</returns>
        public Resource<TRow> GetResource<TRow>(string resourceId) where TRow : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");
            
            return new Resource<TRow>(resourceId, this);
        }

        /// <summary>
        /// Update/Insert the specified payload string using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A string of serialized data.</param>
        /// <param name="dataFormat">One of the data-interchange formats that Socrata supports, into which the payload has been serialized.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult">SodaResult</see> indicating success or failure.</returns>
        public SodaResult Upsert(string payload, SodaDataFormat dataFormat, string resourceId)
        {
            if (dataFormat == SodaDataFormat.XML)
                throw new ArgumentOutOfRangeException("dataFormat", "XML is not a valid format for write operations.");

            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            if (String.IsNullOrEmpty(Username) || String.IsNullOrEmpty(password))
                throw new InvalidOperationException("Write operations require an authenticated client.");

            var uri = SodaUri.ForResourceAPI(Host, resourceId);

            var request = new SodaRequest(uri, "POST", AppToken, Username, password, dataFormat, payload);
            SodaResult result;

            try
            {
                result = request.ParseResponse<SodaResult>();
            }
            catch (WebException webEx)
            {
                string message = unwrapExceptionMessage(webEx);
                result = new SodaResult() { Message = webEx.Message, IsError = true, ErrorCode = message, Data = payload };
            }
            catch (Exception ex)
            {
                result = new SodaResult() { Message = ex.Message, IsError = true, ErrorCode = ex.Message, Data = payload };
            }

            return result;
        }
        
        /// <summary>
        /// Update/Insert the specified collection of entities using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A collection of entities, where each represents a single row in the target resource.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult">SodaResult</see> indicating success or failure.</returns>
        public SodaResult Upsert<T>(IEnumerable<T> payload, string resourceId) where T : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            if (String.IsNullOrEmpty(Username) || String.IsNullOrEmpty(password))
                throw new InvalidOperationException("Write operations require an authenticated client.");

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

            return Upsert(json, SodaDataFormat.JSON, resourceId);
        }
        
        /// <summary>
        /// Update/Insert the specified collection of entities in batches of the specified size, using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A collection of entities, where each represents a single row in the target resource.</param>
        /// <param name="batchSize">The maximum number of entities to process in a single batch.</param>
        /// <param name="breakFunction">A function which, when evaluated true, causes a batch to be sent (possibly before it reaches <paramref name="batchSize"/>).</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A collection of <see cref="SodaResult">SodaResult</see>, one for each batched Upsert.</returns>
        public IEnumerable<SodaResult> BatchUpsert<T>(IEnumerable<T> payload, int batchSize, Func<IEnumerable<T>, T, bool> breakFunction, string resourceId) where T : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            if (String.IsNullOrEmpty(Username) || String.IsNullOrEmpty(password))
                throw new InvalidOperationException("Write operations require an authenticated client.");

            Queue<T> queue = new Queue<T>(payload);

            while (queue.Any())
            {
                var batch = new List<T>();

                for (var index = 0; index < batchSize && queue.Count > 0; index++)
                {
                    if (breakFunction != null && breakFunction(batch, queue.Peek()))
                        break;

                    batch.Add(queue.Dequeue());
                }

                SodaResult result;

                try
                {
                    result = Upsert<T>(batch, resourceId);
                }
                catch (WebException webEx)
                {
                    string message = unwrapExceptionMessage(webEx);
                    result = new SodaResult() { Message = webEx.Message, IsError = true, ErrorCode = message, Data = payload };
                }
                catch (Exception ex)
                {
                    result = new SodaResult() { Message = ex.Message, IsError = true, ErrorCode = ex.Message, Data = payload };
                }

                yield return result;
            }
        }
        
        /// <summary>
        /// Update/Insert the specified collection of entities in batches of the specified size, using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A collection of entities, where each represents a single row in the target resource.</param>
        /// <param name="batchSize">The maximum number of entities to process in a single batch.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A collection of <see cref="SodaResult">SodaResult</see>, one for each batch processed.</returns>
        public IEnumerable<SodaResult> BatchUpsert<T>(IEnumerable<T> payload, int batchSize, string resourceId) where T : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            if (String.IsNullOrEmpty(Username) || String.IsNullOrEmpty(password))
                throw new InvalidOperationException("Write operations require an authenticated client.");

            Func<IEnumerable<T>, T, bool> neverBreak = (a, b) => false;

            return BatchUpsert<T>(payload, batchSize, neverBreak, resourceId);
        }

        /// <summary>
        /// Replace any existing rows with the payload data, using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A string of serialized data.</param>
        /// <param name="dataFormat">One of the data-interchange formats that Socrata supports, into which the payload has been serialized.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult">SodaResult</see> indicating success or failure.</returns>
        public SodaResult Replace(string payload, SodaDataFormat dataFormat, string resourceId)
        {
            if (dataFormat == SodaDataFormat.XML)
                throw new ArgumentOutOfRangeException("dataFormat", "XML is not a valid format for write operations.");

            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            if (String.IsNullOrEmpty(Username) || String.IsNullOrEmpty(password))
                throw new InvalidOperationException("Write operations require an authenticated client.");

            var uri = SodaUri.ForResourceAPI(Host, resourceId);

            var request = new SodaRequest(uri, "PUT", AppToken, Username, password, dataFormat, payload);

            return request.ParseResponse<SodaResult>();
        }

        /// <summary>
        /// Replace any existing rows with a collection of entities, using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A collection of entities, where each represents a single row in the target resource.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult">SodaResult</see> indicating success or failure.</returns>
        public SodaResult Replace<T>(IEnumerable<T> payload, string resourceId) where T : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            if (String.IsNullOrEmpty(Username) || String.IsNullOrEmpty(password))
                throw new InvalidOperationException("Write operations require an authenticated client.");

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

            return Replace(json, SodaDataFormat.JSON, resourceId);
        }
               
        /// <summary>
        /// Delete a single row using the specified row identifier and the specified resource identifier.
        /// </summary>
        /// <param name="rowId">The identifier of the row to be deleted.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult">SodaResult</see> indicating success or failure.</returns>
        public SodaResult DeleteRow(string rowId, string resourceId)
        {
            if (String.IsNullOrEmpty(rowId))
                throw new ArgumentException("Must specify the row to be deleted using its row identifier.", "rowId");

            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            if (String.IsNullOrEmpty(Username) || String.IsNullOrEmpty(password))
                throw new InvalidOperationException("Write operations require an authenticated client.");

            var uri = SodaUri.ForResourceAPI(Host, resourceId, rowId);

            var request = new SodaRequest(uri, "DELETE", AppToken, Username, password);

            return request.ParseResponse<SodaResult>();
        }
    }
}