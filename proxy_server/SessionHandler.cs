﻿using System;
using System.Net.Sockets;
using System.Text;

namespace ProxyServer
{
    internal class SessionHandler
    {
        private TcpClient browser;

        public SessionHandler(TcpClient browser)
        {
            this.browser = browser;
        }

        internal void StartSession()
        {
            NetworkStream browserStream = browser.GetStream();
            string request = GetRequest(browserStream, browser);

            var requestReader = new RequestReader(request);
            requestReader.CheckRequestType();

            if (requestReader.IsConnect)
            {
                HandleConnect(browser, requestReader);
            }
            else if (requestReader.IsGet)
            {
                try
                {
                    NetworkStream hostStream = new TcpClient(requestReader.Host, 80)
                        .GetStream();

                    SendRequest(hostStream, request);
                    HandleResponse(browser, hostStream);
                }
                catch (Exception hostException)
                {
                    browser.Close();
                }

                browser.Close();
            }
        }

        private string GetRequest(NetworkStream stream, TcpClient client)
        {
            string message = null;
            byte[] buffer = null;

            if (client.ReceiveBufferSize > 0)
            {
                buffer = new byte[client.ReceiveBufferSize];
                stream.Read(buffer, 0, client.ReceiveBufferSize);

                message = Encoding.UTF8.GetString(buffer);
                Console.WriteLine("Request: " + message);
            }

            return message;
        }

        private void HandleChunked(TcpClient browser, NetworkStream stream, byte[] bytes)
        {
            var browserStream = new MyNetworkStream(browser.GetStream());
            var networkStream = new MyNetworkStream(stream);
            var chunked = new ChunkedEncoding(browserStream, networkStream);
            chunked.HandleChunked(bytes);
        }

        private void HandleConnect(TcpClient browser, RequestReader requestReader)
        {
            var tunnel = new TlsHandler(browser);
            tunnel.StartHandshake(requestReader.Host, requestReader.Port);
        }

        private void HandleContentLength(
            NetworkStream serverStream,
            NetworkStream browserStream,
            byte[] bodyPart,
            int bodyLength)
        {
            var contentHandler = new ContentLength(
                new MyNetworkStream(serverStream),
                new MyNetworkStream(browserStream));

            contentHandler.HandleResponseBody(bodyPart, Convert.ToString(bodyLength));
        }

        private void HandleResponse(TcpClient browser, NetworkStream serverStream)
        {
            try
            {
                NetworkStream browserStream = browser.GetStream();
                var handleHeaders = new HeadersReader(
                    new MyNetworkStream(serverStream),
                    20000);

                SendResponse(browser, handleHeaders.ReadHeaders());
                byte[] remainder = handleHeaders.Remainder;

                int contentPosition = handleHeaders.ContentLength;
                if (contentPosition != -1)
                {
                    HandleContentLength(
                        serverStream,
                        browserStream,
                        remainder,
                        contentPosition);
                }
                else if (handleHeaders.Chunked)
                {
                    HandleChunked(browser, serverStream, remainder);
                }
            }
            catch (Exception exception)
            {
                serverStream?.Close();
            }
        }

        private void SendRequest(NetworkStream stream, string request)
        {
            stream.Write(Encoding.UTF8.GetBytes(request));
            Console.WriteLine($"Proxy has sent request to host: {request}");
        }

        private void SendResponse(TcpClient browser, byte[] response)
        {
            browser.Client.Send(response);

            string textResponse = Encoding.UTF8.GetString(response);
            Console.WriteLine($"Proxy has sent host response back to browser: {textResponse}");
        }
    }
}