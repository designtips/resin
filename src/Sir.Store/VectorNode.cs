﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sir.Store
{
    /// <summary>
    /// Binary tree where the data is a sparse vector (a word embedding).
    /// The tree is balanced according to cos angles between immediate neighbouring nodes.
    /// </summary>
    public class VectorNode
    {
        public const double IdenticalAngle = 0.99d;
        public const double TrueAngle = 0.95d;
        public const double FalseAngle = 0.5;

        private VectorNode _right;
        private VectorNode _left;
        public long VecOffset { get; set; }
        private HashSet<ulong> _docIds;

        public long PostingsOffset { get; set; }
        public double Angle { get; set; }
        public double Highscore { get; set; }
        public SortedList<char, double> TermVector { get; private set; }
        public VectorNode Ancestor { get; set; }
        public VectorNode Right
        {
            get => _right;
            set
            {
                _right = value;
                _right.Ancestor = this;
            }
        }
        public VectorNode Left
        {
            get => _left;
            set
            {
                _left = value;
                _left.Ancestor = this;
            }
        }

        public VectorNode() 
            : this('\0'.ToString()) { }

        public VectorNode(string s) 
            : this(s.ToVector())
        {
        }

        public VectorNode(SortedList<char, double> wordVector)
        {
            _docIds = new HashSet<ulong>();
            TermVector = wordVector;
            PostingsOffset = -1;
        }

        private byte[][] ToStream()
        {
            var block = new byte[5][];

            byte[] terminator = new byte[1];

            if (Left == null && Right == null)
            {
                terminator[0] = 3;
            }
            else if (Left == null)
            {
                terminator[0] = 2;
            }
            else if (Right == null)
            {
                terminator[0] = 1;
            }
            else
            {
                terminator[0] = 0;
            }

            block[0] = BitConverter.GetBytes(Angle);
            block[1] = BitConverter.GetBytes(VecOffset);
            block[2] = BitConverter.GetBytes(PostingsOffset > -1 ? PostingsOffset : (long)-1);
            block[3] = BitConverter.GetBytes(TermVector.Count);
            block[4] = terminator;

            return block;
        }

        public void Serialize(Stream indexStream, Stream vectorStream, Stream postingsStream)
        {
            VecOffset = TermVector.Serialize(vectorStream);

            var postingsWriter = new PagedPostingsWriter(postingsStream);

            if (_docIds.Count > 0)
            {
                if (PostingsOffset > -1)
                {
                    postingsWriter.Write(PostingsOffset, _docIds.ToList());
                }
                else
                {
                    PostingsOffset = postingsWriter.Write(_docIds.ToList());
                }               
                _docIds.Clear();
            }

            var serialized = ToStream();

            foreach (var buf in serialized)
            {
                indexStream.Write(buf, 0, buf.Length);
            }

            if (Left != null)
            {
                Left.Serialize(indexStream, vectorStream, postingsStream);
            }

            if (Right != null)
            {
                Right.Serialize(indexStream, vectorStream, postingsStream);
            }
        }

        public static VectorNode Deserialize(Stream treeStream, Stream vectorStream)
        {
            const int nodeSize = sizeof(double) + sizeof(long) + sizeof(long) + sizeof(int) + sizeof(byte);
            const int kvpSize = sizeof(char) + sizeof(double);

            var buf = new byte[nodeSize];
            var read = treeStream.Read(buf, 0, buf.Length);

            if (read < nodeSize)
            {
                throw new InvalidDataException();
            }
            
            // Deserialize node
            var angle = BitConverter.ToDouble(buf, 0);
            var vecOffset = BitConverter.ToInt64(buf,       sizeof(double));
            var postingsOffset = BitConverter.ToInt64(buf,  sizeof(double) + sizeof(long));
            var vectorCount = BitConverter.ToInt32(buf,       sizeof(double) + sizeof(long) + sizeof(long));
            var terminator = buf[nodeSize - 1];

            // Deserialize term vector
            var vec = new SortedList<char, double>();
            var vecBuf = new byte[vectorCount * kvpSize];

            vectorStream.Seek(vecOffset, SeekOrigin.Begin);
            vectorStream.Read(vecBuf, 0, vecBuf.Length);

            var offs = 0;

            for (int i = 0; i < vectorCount; i++)
            {
                var key = BitConverter.ToChar(vecBuf, offs);
                var val = BitConverter.ToDouble(vecBuf, offs + sizeof(char));
                vec.Add(key, val);

                offs += kvpSize;
            }

            var node = new VectorNode(vec);
            node.Angle = angle;
            node.PostingsOffset = postingsOffset;

            if (terminator == 0)
            {
                node.Left = Deserialize(treeStream, vectorStream);
                node.Right = Deserialize(treeStream, vectorStream);
            }
            else if (terminator == 1)
            {
                node.Left = Deserialize(treeStream, vectorStream);
            }
            else if (terminator == 2)
            {
                node.Right = Deserialize(treeStream, vectorStream);
            }

            return node;
        }

        public int Depth()
        {
            var count = 0;
            var node = Left;
            while (node != null)
            {
                count++;
                node = node.Left;
            }
            return count;
        }

        public VectorNode ClosestMatch(string word)
        {
            var node = new VectorNode(word);
            return ClosestMatch(node);
        }

        public virtual VectorNode ClosestMatch(VectorNode node)
        {
            var best = this;
            var cursor = this;
            double highscore = 0;

            while (cursor != null)
            {
                var angle = cursor.TermVector.CosAngle(node.TermVector);
                if (angle >= TrueAngle)
                {
                    cursor.Highscore = angle;
                    return cursor;
                }
                else if (angle > FalseAngle)
                {
                    if (angle > highscore)
                    {
                        highscore = angle;
                        best = cursor;
                    }
                    cursor = cursor.Left;
                }
                else
                {
                    cursor = cursor.Right;
                }
            }

            best.Highscore = highscore;
            return best;
        }

        /// <summary>
        /// Add word to index.
        /// </summary>
        /// <returns>True if word is unique, false if it's a duplicate.</returns>
        public bool Add(string word, ulong docId)
        {
            var node = new VectorNode(word);
            node._docIds.Add(docId);
            return Add(node);
        }

        /// <summary>
        /// Add node to tree.
        /// </summary>
        /// <returns>True if node is unique, false if it's a duplicate.</returns>
        public bool Add(VectorNode node)
        {
            node.Angle = node.TermVector.CosAngle(TermVector);

            if (node.Angle >= TrueAngle)
            {
                Merge(node);
                return false;
            }
            else if (node.Angle <= FalseAngle)
            {
                if (Right == null)
                {
                    Right = node;
                    return true;
                }
                else
                {
                    return Right.ClosestMatch(node).Add(node);
                }
            }
            else
            {
                if (Left == null)
                {
                    Left = node;
                    return true;
                }
                else
                {
                    return Left.ClosestMatch(node).Add(node);
                }
            }
        }

        public VectorNode GetRoot()
        {
            var cursor = this;
            while (cursor != null)
            {
                if (cursor.Ancestor == null) break;
                cursor = cursor.Ancestor;
            }
            return cursor;
        }

        private void Merge(VectorNode node)
        {
            var angle = node.TermVector.CosAngle(TermVector);

            //if (angle < IdenticalAngle)
            //{
            //    TermVector = TermVector.Add(node.TermVector);
            //}

            foreach (var id in node._docIds)
            {
                _docIds.Add(id);
            }
        }

        public string Visualize()
        {
            StringBuilder output = new StringBuilder();
            Visualize(this, output, 0);
            return output.ToString();
        }

        private void Visualize(VectorNode node, StringBuilder output, int depth)
        {
            if (node == null) return;

            double angle = 0;

            if (node.Ancestor != null)
            {
                angle = node.Angle;
            }

            output.Append('\t', depth);
            output.AppendFormat(".{0} ({1})", node.ToString(), angle);
            output.AppendLine();

            if (node.Left != null)
                Visualize(node.Left, output, depth + 1);

            if (node.Right != null)
                Visualize(node.Right, output, depth);
        }

        public (int depth, int width) Size()
        {
            var root = this;
            var width = 0;
            var depth = 0;
            var node = root.Right;

            while (node != null)
            {
                var d = node.Depth();
                if (d > depth)
                {
                    depth = d;
                }
                width++;
                node = node.Right;
            }

            return (depth, width);
        }

        public override string ToString()
        {
            var w = new StringBuilder();
            foreach (var c in TermVector)
            {
                w.Append(c.Key);
            }
            return w.ToString();
        }
    }

    public enum NodeType
    {
        Collection = 0,
        Key = 1,
        Word = 2
    }
}
