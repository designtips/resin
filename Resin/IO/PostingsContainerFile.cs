﻿using System;
using System.Collections.Generic;

namespace Resin.IO
{
    [Serializable]
    public class PostingsContainerFile : CompressedFileBase<PostingsContainerFile>
    {
        private readonly string _id;
        public string Id { get { return _id; } }

        private readonly Dictionary<string, PostingsFile> _files;
        /// <summary>
        /// field.token/file
        /// </summary>
        public Dictionary<string, PostingsFile> Files { get { return _files; } }
        public PostingsContainerFile(string id)
        {
            if (id == null) throw new ArgumentNullException("id");
            _id = id;
            _files = new Dictionary<string, PostingsFile>();
        }
    }
}