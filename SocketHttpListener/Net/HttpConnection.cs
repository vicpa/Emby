using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using MediaBrowser.Model.Cryptography;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.System;
using MediaBrowser.Model.Text;
using SocketHttpListener.Primitives;
using System.Security.Authentication;

using System.Threading;
namespace SocketHttpListener.Net
{
    sealed class HttpConnection
    {
        private static AsyncCallback s_onreadCallback = new AsyncCallback(OnRead);
        const int BufferSize = 8192;
        Socket _socket;
        Stream _stream;
        HttpEndPointListener _epl;
        MemoryStream _memoryStream;
        byte[] _buffer;
        HttpListenerContext _context;
        StringBuilder _currentLine;
        ListenerPrefix _prefix;
        HttpRequestStream _requestStream;
        HttpResponseStream _responseStream;
        bool _chunked;
        int _reuses;
        bool _contextBound;
        bool secure;
        int _timeout = 90000; // 90k ms for first request, 15k ms from then on
        private Timer _timer;
        IPEndPoint local_ep;
        HttpListener _lastListener;
        X509Certificate cert;
        SslStream ssl_stream;

        private readonly ILogger _logger;
        private readonly ICryptoProvider _cryptoProvider;
        private readonly IStreamHelper _streamHelper;
        private readonly ITextEncoding _textEncoding;
        private readonly IFileSystem _fileSystem;
        private readonly IEnvironmentInfo _environment;

        public HttpConnection(ILogger logger, Socket socket, HttpEndPointListener epl, bool secure, X509Certificate cert, ICryptoProvider cryptoProvider, IStreamHelper streamHelper, ITextEncoding textEncoding, IFileSystem fileSystem, IEnvironmentInfo environment)
        {
            _logger = logger;
            this._socket = socket;
            this._epl = epl;
            this.secure = secure;
            this.cert = cert;
            _cryptoProvider = cryptoProvider;
            _streamHelper = streamHelper;
            _textEncoding = textEncoding;
            _fileSystem = fileSystem;
            _environment = environment;

            if (secure == false)
            {
                _stream = new SocketStream(_socket, false);
            }
            else
            {
                ssl_stream = new SslStream(new SocketStream(_socket, false), false, (t, c, ch, e) =>
                {
                    if (c == null)
                    {
                        return true;
                    }

                    //var c2 = c as X509Certificate2;
                    //if (c2 == null)
                    //{
                    //    c2 = new X509Certificate2(c.GetRawCertData());
                    //}

                    //_clientCert = c2;
                    //_clientCertErrors = new int[] { (int)e };
                    return true;
                });

                _stream = ssl_stream;
            }
        }

        public Stream Stream
        {
            get
            {
                return _stream;
            }
        }

        public async Task Init()
        {
            _timer = new Timer(OnTimeout, null, Timeout.Infinite, Timeout.Infinite);

            if (ssl_stream != null)
            {
                var enableAsync = true;
                if (enableAsync)
                {
                    await ssl_stream.AuthenticateAsServerAsync(cert, false, (SslProtocols)ServicePointManager.SecurityProtocol, false).ConfigureAwait(false);
                }
                else
                {
                    ssl_stream.AuthenticateAsServer(cert, false, (SslProtocols)ServicePointManager.SecurityProtocol, false);
                }
            }

            InitInternal();
        }

        private void InitInternal()
        {
            _contextBound = false;
            _requestStream = null;
            _responseStream = null;
            _prefix = null;
            _chunked = false;
            _memoryStream = new MemoryStream();
            _position = 0;
            _inputState = InputState.RequestLine;
            _lineState = LineState.None;
            _context = new HttpListenerContext(this, _textEncoding);
        }

        public bool IsClosed
        {
            get { return (_socket == null); }
        }

        public int Reuses
        {
            get { return _reuses; }
        }

        public IPEndPoint LocalEndPoint
        {
            get
            {
                if (local_ep != null)
                    return local_ep;

                local_ep = (IPEndPoint)_socket.LocalEndPoint;
                return local_ep;
            }
        }

        public IPEndPoint RemoteEndPoint
        {
            get { return _socket.RemoteEndPoint as IPEndPoint; }
        }

        public bool IsSecure
        {
            get { return secure; }
        }

        public ListenerPrefix Prefix
        {
            get { return _prefix; }
            set { _prefix = value; }
        }

        private void OnTimeout(object unused)
        {
            //_logger.Info("HttpConnection timer fired");
            CloseSocket();
            Unbind();
        }

        public void BeginReadRequest()
        {
            if (_buffer == null)
                _buffer = new byte[BufferSize];
            try
            {
                if (_reuses == 1)
                    _timeout = 15000;
                //_timer.Change(_timeout, Timeout.Infinite);
                _stream.BeginRead(_buffer, 0, BufferSize, s_onreadCallback, this);
            }
            catch
            {
                //_timer.Change(Timeout.Infinite, Timeout.Infinite);
                CloseSocket();
                Unbind();
            }
        }

        public HttpRequestStream GetRequestStream(bool chunked, long contentlength)
        {
            if (_requestStream == null)
            {
                byte[] buffer = _memoryStream.GetBuffer();
                int length = (int)_memoryStream.Length;
                _memoryStream = null;
                if (chunked)
                {
                    _chunked = true;
                    //_context.Response.SendChunked = true;
                    _requestStream = new ChunkedInputStream(_context, _stream, buffer, _position, length - _position);
                }
                else
                {
                    _requestStream = new HttpRequestStream(_stream, buffer, _position, length - _position, contentlength);
                }
            }
            return _requestStream;
        }

        public HttpResponseStream GetResponseStream(bool isExpect100Continue = false)
        {
            // TODO: can we get this _stream before reading the input?
            if (_responseStream == null)
            {
                var supportsDirectSocketAccess = !_context.Response.SendChunked && !isExpect100Continue && !secure;

                _responseStream = new HttpResponseStream(_stream, _context.Response, false, _streamHelper, _socket, supportsDirectSocketAccess, _environment, _fileSystem, _logger);
            }
            return _responseStream;
        }

        private static void OnRead(IAsyncResult ares)
        {
            HttpConnection cnc = (HttpConnection)ares.AsyncState;
            cnc.OnReadInternal(ares);
        }

        private void OnReadInternal(IAsyncResult ares)
        {
            //_timer.Change(Timeout.Infinite, Timeout.Infinite);
            int nread = -1;
            try
            {
                nread = _stream.EndRead(ares);
                _memoryStream.Write(_buffer, 0, nread);
                if (_memoryStream.Length > 32768)
                {
                    SendError("Bad Request", 400);
                    Close(true);
                    return;
                }
            }
            catch
            {
                if (_memoryStream != null && _memoryStream.Length > 0)
                    SendError();
                if (_socket != null)
                {
                    CloseSocket();
                    Unbind();
                }
                return;
            }

            if (nread == 0)
            {
                CloseSocket();
                Unbind();
                return;
            }

            if (ProcessInput(_memoryStream))
            {
                if (!_context.HaveError)
                    _context.Request.FinishInitialization();

                if (_context.HaveError)
                {
                    SendError();
                    Close(true);
                    return;
                }

                if (!_epl.BindContext(_context))
                {
                    const int NotFoundErrorCode = 404;
                    SendError(HttpStatusDescription.Get(NotFoundErrorCode), NotFoundErrorCode);
                    Close(true);
                    return;
                }
                HttpListener listener = _epl.Listener;
                if (_lastListener != listener)
                {
                    RemoveConnection();
                    listener.AddConnection(this);
                    _lastListener = listener;
                }

                _contextBound = true;
                listener.RegisterContext(_context);
                return;
            }
            _stream.BeginRead(_buffer, 0, BufferSize, s_onreadCallback, this);
        }

        private void RemoveConnection()
        {
            if (_lastListener == null)
                _epl.RemoveConnection(this);
            else
                _lastListener.RemoveConnection(this);
        }

        private enum InputState
        {
            RequestLine,
            Headers
        }

        private enum LineState
        {
            None,
            CR,
            LF
        }

        InputState _inputState = InputState.RequestLine;
        LineState _lineState = LineState.None;
        int _position;

        // true -> done processing
        // false -> need more input
        private bool ProcessInput(MemoryStream ms)
        {
            byte[] buffer = ms.GetBuffer();
            int len = (int)ms.Length;
            int used = 0;
            string line;

            while (true)
            {
                if (_context.HaveError)
                    return true;

                if (_position >= len)
                    break;

                try
                {
                    line = ReadLine(buffer, _position, len - _position, ref used);
                    _position += used;
                }
                catch
                {
                    _context.ErrorMessage = "Bad request";
                    _context.ErrorStatus = 400;
                    return true;
                }

                if (line == null)
                    break;

                if (line == "")
                {
                    if (_inputState == InputState.RequestLine)
                        continue;
                    _currentLine = null;
                    ms = null;
                    return true;
                }

                if (_inputState == InputState.RequestLine)
                {
                    _context.Request.SetRequestLine(line);
                    _inputState = InputState.Headers;
                }
                else
                {
                    try
                    {
                        _context.Request.AddHeader(line);
                    }
                    catch (Exception e)
                    {
                        _context.ErrorMessage = e.Message;
                        _context.ErrorStatus = 400;
                        return true;
                    }
                }
            }

            if (used == len)
            {
                ms.SetLength(0);
                _position = 0;
            }
            return false;
        }

        private string ReadLine(byte[] buffer, int offset, int len, ref int used)
        {
            if (_currentLine == null)
                _currentLine = new StringBuilder(128);
            int last = offset + len;
            used = 0;
            for (int i = offset; i < last && _lineState != LineState.LF; i++)
            {
                used++;
                byte b = buffer[i];
                if (b == 13)
                {
                    _lineState = LineState.CR;
                }
                else if (b == 10)
                {
                    _lineState = LineState.LF;
                }
                else
                {
                    _currentLine.Append((char)b);
                }
            }

            string result = null;
            if (_lineState == LineState.LF)
            {
                _lineState = LineState.None;
                result = _currentLine.ToString();
                _currentLine.Length = 0;
            }

            return result;
        }

        public void SendError(string msg, int status)
        {
            try
            {
                HttpListenerResponse response = _context.Response;
                response.StatusCode = status;
                response.ContentType = "text/html";
                string description = HttpStatusDescription.Get(status);
                string str;
                if (msg != null)
                    str = string.Format("<h1>{0} ({1})</h1>", description, msg);
                else
                    str = string.Format("<h1>{0}</h1>", description);

                byte[] error = _textEncoding.GetDefaultEncoding().GetBytes(str);
                response.Close(error, false);
            }
            catch
            {
                // response was already closed
            }
        }

        public void SendError()
        {
            SendError(_context.ErrorMessage, _context.ErrorStatus);
        }

        private void Unbind()
        {
            if (_contextBound)
            {
                _epl.UnbindContext(_context);
                _contextBound = false;
            }
        }

        public void Close()
        {
            Close(false);
        }

        private void CloseSocket()
        {
            if (_socket == null)
                return;

            try
            {
                _socket.Close();
            }
            catch { }
            finally
            {
                _socket = null;
            }

            RemoveConnection();
        }

        internal void Close(bool force)
        {
            if (_socket != null)
            {
                Stream st = GetResponseStream();
                if (st != null)
                    st.Close();

                _responseStream = null;
            }

            if (_socket != null)
            {
                force |= !_context.Request.KeepAlive;
                if (!force)
                    force = (string.Equals(_context.Response.Headers["connection"], "close", StringComparison.OrdinalIgnoreCase));

                if (!force && _context.Request.FlushInput())
                {
                    if (_chunked && _context.Response.ForceCloseChunked == false)
                    {
                        // Don't close. Keep working.
                        _reuses++;
                        Unbind();
                        InitInternal();
                        BeginReadRequest();
                        return;
                    }

                    _reuses++;
                    Unbind();
                    InitInternal();
                    BeginReadRequest();
                    return;
                }

                Socket s = _socket;
                _socket = null;
                try
                {
                    if (s != null)
                        s.Shutdown(SocketShutdown.Both);
                }
                catch
                {
                }
                finally
                {
                    if (s != null)
                    {
                        try
                        {
                            s.Close();
                        }
                        catch { }
                    }
                }
                Unbind();
                RemoveConnection();
                return;
            }
        }
    }
}