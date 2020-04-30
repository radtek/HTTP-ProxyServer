﻿using ProxyHTTP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ProxyServer
{
    public class ProxyServer
    {
        private readonly string proxyIP;

        public ProxyServer(string proxyIP)
        {
            this.proxyIP = proxyIP;
        }

        public void StartProxy()
        {
            TcpListener proxy = new TcpListener(IPAddress.Parse(proxyIP), 8080);

            proxy.Start();

            while (true)
            {
                Console.Write("Waiting for web client... ");

                TcpClient client = proxy.AcceptTcpClient();
                Console.WriteLine("Connected!");

                NetworkStream browserStream = client.GetStream();
                string request = GetRequest(browserStream, client);

                if (CheckRequest(request))
                {
                    try
                    {
                        string host = GetHost(request);
                        NetworkStream stream = new TcpClient(host, 80).GetStream();
                        SendRequest(stream, request);
                        HandleResponse(client, stream);
                    }
                    catch (Exception hostException)
                    {
                        //hostName does not exist
                    }
                }
            }
        }

        private bool CheckRequest(string request)
        {
            return request?.StartsWith("GET ") == true;
        }

        private string GetHost(string request)
        {
            string[] requestDetails = request.Trim().Split('\n');
            string hostHeader = requestDetails[1];
            int lastPosition = hostHeader.LastIndexOf('\r');

            return hostHeader.Substring(6, lastPosition - 6);
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
            byte[] readLength = SendHeadersPart(browser, stream, bytes, new HttpParser(bytes));

             var chunkReader = new HttpParser(bytes);
             byte[] readLine = chunkReader.ReadLine(Headers.NewLine);
             string chunkLine = Encoding.UTF8.GetString(readLine);

            /*while (chunkLine != Headers.ZeroChunk)
             {
                 while (!chunkReader.IsChunkComplete(readLine, Headers.NewLine, 3))
                 {
                     var newBytes = new byte[512];
                     int toRead = stream.Read(newBytes, 0, 512);
                     bytes = bytes.Concat(newBytes.Take(toRead)).ToArray();
                     chunkReader = new HttpParser(bytes);
                     readLine = chunkReader.ReadLine(Headers.NewLine);
                 }

                 int lineLength = readLine.Length;
                 int chunkSize = Convert.ToInt32(
                              chunkLine.Substring(0, lineLength - 2),
                              16);

                 SendResponse(browser, readLine);
                 SendChunkToBrowser(stream, browser, chunkSize, bytes.Skip(lineLength).ToArray());

                 bytes = new byte[512];
                 int readFromStream = stream.Read(bytes, 0, 512);
                 chunkReader = new HttpParser(bytes.Take(readFromStream).ToArray());
                 readLine = chunkReader.ReadLine(Headers.NewLine);
                 chunkLine = Encoding.UTF8.GetString(readLine);
             }

             var endingChunk = chunkReader.ReadLine(Headers.NewLine);
             SendResponse(browser, endingChunk);
             if (endingChunk.Length > 2)
             {
                 SendResponse(browser, chunkReader.ReadLine(Headers.EmptyLine));
             }*/
        }

        private void HandleContentLength(
            TcpClient browser,
            NetworkStream stream,
            byte[] buffer,
            int bodyLength)
        {
            byte[] remainingBytes = SendHeadersPart(browser, stream, buffer, new HttpParser(buffer));

            SendChunkToBrowser(
                stream,
                browser,
                bodyLength,
                remainingBytes.ToArray());

            browser.Close();
        }

        private byte[] SendHeadersPart(
            TcpClient browser,
            NetworkStream stream,
            byte[] buffer,
            HttpParser parser)
        {
            byte[] readLine = parser.ReadLine(Headers.EmptyLine);

            while (readLine.Length == buffer.Length)
            {
                SendResponse(browser, readLine);
                int readFromStream = stream.Read(buffer, 0, buffer.Length);
                parser = new HttpParser(buffer.Take(readFromStream).ToArray());
                readLine = parser.ReadLine(Headers.EmptyLine);
            }

            SendResponse(browser, readLine);
            return buffer.Skip(readLine.Length).ToArray();
        }

        private void HandleResponse(TcpClient browser, NetworkStream stream)
        {
            byte[] buffer = new byte[512];
            int readFromStream = 0;
            int countHeaderBytes = 0;

            try
            {
                if (stream.CanRead)
                {
                    readFromStream = stream.Read(buffer, 0, buffer.Length);
                }

                var responseReader = new HttpParser(buffer);
                byte[] readLine = responseReader.ReadLine(Headers.NewLine);
                Console.Write(Encoding.UTF8.GetString(readLine));

                while (!readLine.SequenceEqual(Headers.NewLineByte))
                {
                    SendResponse(browser, readLine);
                    countHeaderBytes += readLine.Length;

                    if (responseReader.Check(readLine, Headers.ContentHeader))
                    {
                        int bodyLength = responseReader.GetContentLength(readLine);
                        HandleContentLength(
                            browser,
                            stream,
                            buffer.Skip(countHeaderBytes).Take(readFromStream - countHeaderBytes).ToArray(),
                            bodyLength);
                    }
                    else if (responseReader.Check(readLine, Headers.ChunkedHeader, Headers.Chunked))
                    {
                        HandleChunked(
                            browser,
                            stream,
                            buffer.Skip(countHeaderBytes).Take(readFromStream - countHeaderBytes).ToArray());
                    }

                    readLine = responseReader.ReadLine(Headers.NewLine);
                    Console.Write(Encoding.UTF8.GetString(readLine));
                }

                browser.Close();
            }
            catch (Exception exception)
            {
                stream?.Close();
            }
        }

        private List<byte[]> MakeBufferList(NetworkStream stream, int chunkSize, byte[] bytes)
        {
            int readFromStream = 0, bytesLength = bytes.Length;
            var listElement = new byte[bytesLength];
            int listSize = (chunkSize / bytesLength) + 1;
            List<byte[]> bufferList = new List<byte[]>(listSize)
            {
                bytes
            };

            if (chunkSize <= bytesLength)
            {
                return bufferList;
            }

            int remainingBytes = chunkSize - bytesLength;
            while (remainingBytes > bytesLength)
            {
                readFromStream = stream.Read(listElement, 0, bytesLength);
                bufferList.Add(listElement.Take(readFromStream).ToArray());
                remainingBytes -= bytesLength;
            }

            readFromStream = stream.Read(listElement, 0, remainingBytes + 2);
            bufferList.Add(listElement.Take(readFromStream).ToArray());

            return bufferList;
        }

        private void SendChunkToBrowser(NetworkStream stream, TcpClient browser, int chunkSize, byte[] bytes)
        {
            foreach (var buffer in MakeBufferList(stream, chunkSize, bytes))
            {
                SendResponse(browser, buffer);
            }
        }

        private void SendRequest(NetworkStream stream, string request)
        {
            stream.Write(Encoding.UTF8.GetBytes(request));
            Console.WriteLine($"Proxy has sent request to host: {request}");
        }

        private void SendResponse(TcpClient browserClient, byte[] response)
        {
            browserClient.Client.Send(response);

            string textResponse = Encoding.UTF8.GetString(response);
            Console.WriteLine($"Proxy has sent host response back to browser: {textResponse}");
        }
    }
}