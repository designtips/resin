﻿using System;
using System.Collections.Generic;
using System.IO;

namespace Sir.Store
{
    /// <summary>
    /// Postings stream reader that lets you get a list of document references,
    /// but only if you know the list's position in the stream and its length.
    /// </summary>
    public class PostingsReader
    {
        private readonly Stream _stream;

        public PostingsReader(Stream stream)
        {
            _stream = stream;
        }

        public IEnumerable<ulong> Read(long offset, int len)
        {
            _stream.Seek(offset, SeekOrigin.Begin);

            var buf = new byte[len];
            var read = _stream.Read(buf, 0, len);

            if (read != len)
            {
                throw new InvalidDataException();
            }

            var position = 0;

            while (position < len)
            {
                var docId = BitConverter.ToUInt64(buf, position);
                var status = buf[position + sizeof(ulong)];

                if (status == 1)
                    yield return docId;

                position += (sizeof(ulong) + sizeof(byte));
            }
            
        }
    }
}
