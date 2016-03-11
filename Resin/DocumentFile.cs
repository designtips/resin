﻿using System;
using System.Collections.Generic;
using System.IO;
using ProtoBuf;

namespace Resin
{
    public class DocumentFile : IDisposable
    {
        private readonly string _fileName;
        private readonly IDictionary<string, IList<string>> _doc;
 
        public DocumentFile(string fileName, bool overwrite)
        {
            _fileName = fileName;
            if (!overwrite && File.Exists(fileName))
            {
                using (var file = File.OpenRead(fileName))
                {
                    _doc = Serializer.Deserialize<Dictionary<string, IList<string>>>(file);
                }
            }
            else
            {
                _doc = new Dictionary<string, IList<string>>();
            }
        }

        public void Write(string fieldName, string fieldValue)
        {
            IList<string> values;
            if (!_doc.TryGetValue(fieldName, out values))
            {
                values = new List<string> {fieldValue};
                _doc.Add(fieldName, values);
            }
            else
            {
                values.Add(fieldValue);
            }
        }

        private void Flush()
        {
            var dir = Path.GetDirectoryName(_fileName);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using (var fs = File.Create(_fileName))
            {
                Serializer.Serialize(fs, _doc);
            }
        }

        public void Dispose()
        {
            Flush();
        }
    }
}