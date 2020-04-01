﻿using ProxyHTTP;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace ProxyHTTP_Facts
{
    public class HttpReaderFacts
    {
        private const string LineSeparator = "\r\n";

        [Fact]
        public void Should_Correctly_ReadLine_For_EmptyLINE()
        {
            // Given
            const string data = "\r\n\r\n";

            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            byte[] buffer = new byte[8];
            stream.Read(buffer, 0, 4);

            var chunkReader = new HttpReader(buffer, LineSeparator);

            // When
            string line = Encoding.UTF8.GetString(chunkReader.ReadLine());

            // Then
            Assert.Equal(data, line);
        }

        [Fact]
        public void Should_Return_RemainingBytes_When_Chunk_Is_NOT_Complete()
        {
            // Given
            const string data = "3\r\nab";
            byte[] toCheck = Encoding.UTF8.GetBytes("ab");

            MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(data));
            byte[] buffer = new byte[5];
            stream.Read(buffer, 0, 5);

            var chunkReader = new HttpReader(buffer, LineSeparator);

            // When
            string line = Encoding.UTF8.GetString(chunkReader.ReadLine());
            byte[] byteLine = chunkReader.ReadBytes(line);

            // Then
            Assert.True(byteLine.SequenceEqual(toCheck));
            Assert.False(chunkReader.IsChunkComplete(byteLine));
        }

        [Fact]
        public void Test_ChunkReader_Should_Read_LeadingLine_for_given_CHUNK()
        {
            // Given
            const string data = "322345\r\nabc";
            var stream = new MemoryStream(Encoding.ASCII.GetBytes(data));
            byte[] buffer = new byte[11];

            // When
            stream.Read(buffer, 0, 11);
            var chunkReader = new HttpReader(buffer, LineSeparator);
            byte[] line = chunkReader.ReadLine();

            // Then
            Assert.Equal("322345\r\n", Encoding.UTF8.GetString(line));
        }

        [Fact]
        public void Test_ReadBytes_Should_Do_Multiple_Reads()
        {
            // Given
            const string data = "2b2\r\nabc\r\n3\r\n222\r\n";
            MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(data));
            byte[] buffer = new byte[32];
            stream.Read(buffer, 0, 32);

            var chunkReader = new HttpReader(buffer, LineSeparator);

            // When
            chunkReader.ReadLine();
            chunkReader.ReadLine();
            string line = Encoding.UTF8.GetString(chunkReader.ReadLine());
            string byteLine = Encoding.UTF8.GetString(chunkReader.ReadBytes(line));

            // Then
            Assert.Equal("222\r\n", byteLine);
        }

        [Fact]
        public void Test_ReadBytes_Should_Read_Bytes_for_given_chunk_SIZE()
        {
            // Given
            const string data = "3\r\nabc\r\n2b";
            MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(data));
            byte[] buffer = new byte[32];
            stream.Read(buffer, 0, 32);
            var chunkReader = new HttpReader(buffer, LineSeparator);

            // When
            string line = Encoding.UTF8.GetString(chunkReader.ReadLine());
            string byteLine = Encoding.UTF8.GetString(chunkReader.ReadBytes(line));

            // Then
            Assert.Equal("abc\r\n", byteLine);
        }
    }
}