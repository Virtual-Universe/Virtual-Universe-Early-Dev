/* 24 April 2019 
 * 
 * Nani made this.
*/

using HttpServer;
using OpenMetaverse;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections;

namespace OpenSim.Framework.Servers.HttpServer
{
    public delegate Hashtable DirectProcessMethod(UUID agentID, Hashtable request);

    public class NDirectHttpHandler : IDirectServiceHttpHandler
    {
        private ManualResetEvent m_signal = new ManualResetEvent(false);

        private class NDirectHttpRequest
        {
            public BaseHttpServer Server;
            public int RequestTime;
            public Hashtable Params;
            public IHttpClientContext HttpContext;
            public IHttpRequest Request;
        }

        public string Name { get; private set; }

        /// <summary>
        /// Description for this handler.
        /// </summary>
        /// <remarks>
        /// Used for diagnostics.  The path doesn't always describe what the handler does.  Can be null if none
        /// specified.
        /// </remarks>
        public string Description { get; private set; }

        // Return response content type
        public string ContentType { get; private set; }

        // Return required http method
        public string HttpMethod { get; private set; }

        // Return path
        public string Path { get; private set; }

        /// <summary>
        /// Number of requests received by this handler
        /// </summary>
        public int RequestsReceived { get; private set; }

        /// <summary>
        /// Number of requests handled.
        /// </summary>
        /// <remarks>
        /// Should be equal to RequestsReceived unless requested are being handled slowly or there is deadlock.
        /// </remarks>
        public int RequestsHandled { get; private set; }

        private readonly UUID m_agentID;
        private readonly BaseHttpServer m_httpServer;
        private readonly DirectProcessMethod ProcessRequest;

        // When a request takes 30 seconds to be added to the action chain
        // we send a fail back to the viewer so it can ask again.
        private const int _TIMEOUT = 300000;

        // RequestQueue is global!
        private static readonly NActionChain RequestQueue = new NActionChain(32, true,
                                                                ThreadPriority.Normal);
        private bool m_running = false;
        private Thread m_Thread = null;

        private NSingleReaderCountlessQueue<NDirectHttpRequest> personalQueue = 
            new NSingleReaderCountlessQueue<NDirectHttpRequest>();
        
        public NDirectHttpHandler(string path, 
                                  BaseHttpServer httpServer,
                                  UUID agentID, 
                                  DirectProcessMethod processMethode )
        {
            m_httpServer = httpServer;
            m_agentID = agentID;

            ProcessRequest = processMethode;

            HttpMethod = "GET";

            Path = path;
            Name = Description = string.Empty;
            RequestsReceived = 0;
            RequestsHandled = 0;

            m_running = true;
            try
            {
                // Just one single thread (per avatar), 
                // which is why we can use NSingleReaderCountlessQueue.
                m_Thread = new Thread(doRequests);
                m_Thread.Priority = ThreadPriority.Normal;
                m_Thread.IsBackground = true;
                m_Thread.Start();
            }
            catch { }
        }

        ~NDirectHttpHandler()
        {
            Stop();
        }

        private bool CheckTimeout(NDirectHttpRequest req)
        {
            try
            {
                // Reaching this timeout would mean that a request has been
                // sitting in the queue for a long time and still has not
                // been send to the action chain to be dealt with.
                // Best thing to do is NOT to go fecth it now but to tell the
                // awaiting viewer as soon as possible to ask again for the
                // asset (texture/mesh/inventory desctription).
                // That results in a much nicer viewing experience.
                if (Environment.TickCount - req.RequestTime > _TIMEOUT)
                {
                    req = null;

                    Hashtable response = new Hashtable();
                    response["int_response_code"] = 503;
                    response["str_response_string"] = "Throttled";
                    response["content_type"] = "text/plain";
                    response["keepalive"] = false;

                    Hashtable headers = new Hashtable();
                    headers["Retry-After"] = 10; // 10 seconds. :)
                    response["headers"] = headers;

                    DoHTTPGruntWork(req.Server, response, req.HttpContext, req.Request);
                    response = null;
                    headers = null;
                    return false;
                }

                return true; // We are inside the timeout.
            }
            catch { }
            return false;
        }

        private void doRequests()
        {
            while (m_running)
            {
                try
                {
                    NDirectHttpRequest req;
                    if (personalQueue.Dequeue(out req) && m_running)
                    {
                        if (CheckTimeout(req))
                        {
                            // Now we hand the request of to the multi threaded
                            // action chain to be processed.
                            RequestQueue.Enqueue(
                                delegate
                                {
                                    Hashtable responseData = ProcessRequest(m_agentID, req.Params);
                                    DoHTTPGruntWork(req.Server, responseData, req.HttpContext, req.Request);
                                    responseData = null;
                                    req = null;
                                });
                        }

                        RequestsHandled++;
                    }

                    if (m_running)
                    {
                        if (personalQueue.isEmpty)
                        { 
                            m_signal.Reset();
                            if (m_running && personalQueue.isEmpty)
                            {
                                m_signal.WaitOne();
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void Enqueue( NDirectHttpRequest req )
        {
            RequestsReceived++;
            personalQueue.Enqueue(req);
            m_signal.Set();
        }

        private void Stop()
        {
            try
            {
                m_running = false;
                m_signal.Set();

                personalQueue.Clear();
                personalQueue = null;
            } catch { }
        }

        public void Handle(IHttpClientContext context, IHttpRequest request)
        {
            Hashtable keysvals = new Hashtable();
            try
            {
                OSHttpRequest req = new OSHttpRequest(context, request);

                Stream requestStream = req.InputStream;

                string requestBody;
                Encoding encoding = Encoding.UTF8;
                using (StreamReader reader = new StreamReader(requestStream, encoding))
                {
                    requestBody = reader.ReadToEnd();
                }

                Hashtable headervals = new Hashtable();

                string[] querystringkeys = req.QueryString.AllKeys;
                string[] rHeaders = req.Headers.AllKeys;

                keysvals.Add("body", requestBody);
                keysvals.Add("uri", req.RawUrl);
                keysvals.Add("content-type", req.ContentType);
                keysvals.Add("http-method", req.HttpMethod);

                foreach (string queryname in querystringkeys)
                {
                    keysvals.Add(queryname, req.QueryString[queryname]);
                }

                foreach (string headername in rHeaders)
                {
                    headervals[headername] = req.Headers[headername];
                }

                keysvals.Add("headers", headervals);
                keysvals.Add("querystringkeys", querystringkeys);

                Enqueue(new NDirectHttpRequest()
                {
                    RequestTime = Environment.TickCount,
                    Server = m_httpServer,
                    Params = keysvals,
                    HttpContext = context,
                    Request = request
                });
            }
            catch
            {
                return; // hmm is this safe? Just leaving like that?
            }
        }

        internal void DoHTTPGruntWork( BaseHttpServer server, 
                                       Hashtable responsedata,
                                       IHttpClientContext HttpContext,
                                       IHttpRequest Request )
        {
            try
            {
                OSHttpResponse response
                    = new OSHttpResponse(new HttpResponse(HttpContext, Request), HttpContext);

                byte[] buffer = server.DoHTTPGruntWork(responsedata, response);

                if (Request.Body.CanRead)
                    Request.Body.Dispose();

                response.SendChunked = false;
                response.ContentLength64 = buffer.Length;
                response.ContentEncoding = Encoding.UTF8;
                response.ReuseContext = false;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Flush();
                response.Send();
                buffer = null;
            }
            catch { }

            RequestsHandled++;
        }
    }
}
