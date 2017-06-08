﻿using log4net;
using Resin.Analysis;
using Resin.IO;
using Resin.IO.Read;
using Resin.Sys;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Resin
{
    public class MergeOperation : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(MergeOperation));
        private DocHashReader _hashReader;
        private DocumentAddressReader _addressReader;
        private DocumentReader _documentReader;
        private readonly string _directory;
        private readonly string[] _ixFilesToDelete;

        public MergeOperation(string directory)
        {
            _directory = directory;
            var ixs = Util.GetIndexFileNamesInChronologicalOrder(_directory).Take(2).ToList();
            _ixFilesToDelete = ixs.ToArray();
        }

        public void Dispose()
        {
            _hashReader.Dispose();
            _addressReader.Dispose();
            _documentReader.Dispose();

            foreach(var file in _ixFilesToDelete)
            {
                Remove(file);
            }
        }

        public long Merge (Compression compression, string primaryKeyFieldName)
        {
            if (_ixFilesToDelete.Length == 1) return IxInfo.Load(_ixFilesToDelete[0]).VersionId;

            return Merge(
                _ixFilesToDelete[0], 
                _ixFilesToDelete[1], 
                _directory, 
                compression, 
                primaryKeyFieldName);
        }

        public long Merge(
            string firstIndexFileName, 
            string secondIndexFileName, 
            string outputDirectory, 
            Compression compression, 
            string primaryKeyFieldName)
        {
            Log.Info("merging");

            var documents = StreamDocuments(secondIndexFileName)
                .Concat(StreamDocuments(firstIndexFileName));

            var documentStream = new InMemoryDocumentStream(documents);
            long versionId;

            using (var upsert = new UpsertOperation
                (outputDirectory,
                new Analyzer(),
                compression,
                documentStream))
            {
                versionId = upsert.Write();
            }

            return versionId;
        }

        private void Remove(string ixFileName)
        {
            //TODO: create lock file
            File.Delete(ixFileName);

            var dir = Path.GetDirectoryName(ixFileName);
            var name = Path.GetFileNameWithoutExtension(ixFileName);

            foreach(var file in Directory.GetFiles(dir, name + ".*"))
            {
                File.Delete(file);
            }
        }

        private IEnumerable<Document> StreamDocuments(string ixFileName)
        {
            var dir = Path.GetDirectoryName(ixFileName);
            var ix = IxInfo.Load(ixFileName);
            var docFileName = Path.Combine(dir, ix.VersionId + ".rdoc");
            var docAddressFn = Path.Combine(dir, ix.VersionId + ".da");
            var docHashesFileName = Path.Combine(dir, string.Format("{0}.{1}", ix.VersionId, "pk"));

            return StreamDocuments(
                docFileName, docAddressFn, docHashesFileName, ix.DocumentCount, ix.Compression);
        }

        private IEnumerable<Document> StreamDocuments(
            string docFileName, 
            string docAddressFn, 
            string docHashesFileName, 
            int numOfDocs,
            Compression compression)
        {
            _hashReader = new DocHashReader(docHashesFileName);
            _addressReader = new DocumentAddressReader(new FileStream(docAddressFn, FileMode.Open, FileAccess.Read));
            _documentReader = new DocumentReader(new FileStream(docFileName, FileMode.Open, FileAccess.Read), compression);

            return StreamDocuments(_hashReader, _addressReader, _documentReader, numOfDocs);
        }

        private IEnumerable<Document> StreamDocuments(
            DocHashReader hashReader, 
            DocumentAddressReader addressReader, 
            DocumentReader documentReader,
            int numOfDocs)
        {
            for (int docId = 0; docId < numOfDocs; docId++)
            {
                var hash = hashReader.Read(docId);

                var address = addressReader.Read(new[] 
                {
                    new BlockInfo(docId * Serializer.SizeOfBlock(), Serializer.SizeOfBlock())
                }).First();

                var document = documentReader.Read(new List<BlockInfo> { address }).First();

                if (!hash.IsObsolete)
                {
                    yield return document;
                }
            }
        }
    }
}