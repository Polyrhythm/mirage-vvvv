﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SlimDX;
using VVVV.Utils.VMath;

using VVVV.Mirage.Lib.Scene;
//using VVVV.Mirage.Lib.Util;

namespace VVVV.Mirage.Lib.Util
{
    /*public struct BVHNode
    {
        public Vector3D min;
        public int refA;
        public Vector3D max;
        public int refB;
    }*/

    public class BVHBuilder
    {
        private LBVH bvh;
        private LeafData[] leafData;
        private uint[] mortonCodes;

        private class LeafData
        {
            public IEntity entity;
            public int index;
            public AABB bounds;

            public LeafData(IEntity entity, int index, AABB bounds)
            {
                this.entity = entity;
                this.index = index;
                this.bounds = bounds;
            }
        }

        private uint expandBits(uint v)
        {
            v = (v * 0x00010001u) & 0xFF0000FFu;
            v = (v * 0x00000101u) & 0x0F00F00Fu;
            v = (v * 0x00000011u) & 0xC30C30C3u;
            v = (v * 0x00000005u) & 0x49249249u;
            return v;
        }

        public uint getMortonCode(Vector3D p)
        {
            p = VMath.Clamp(p*1023.0, 0.0, 1023.0);
            uint xx = expandBits((uint)p.x);
            uint yy = expandBits((uint)p.y);
            uint zz = expandBits((uint)p.z);
            return (xx << 2) | (yy << 1) | (zz);
        }

        private uint clz(int x)
        {
            uint n = 0;
            if (x == 0) return 32;
            while (true)
            {
                if (x < 0) break;
                n++;
                x <<= 1;
            }
            return n;
        }

        private uint findSplit(uint[] mortonCodes, uint first, uint last)
        {
            uint firstCode = mortonCodes[first];
            uint lastCode = mortonCodes[last];

            if (firstCode == lastCode)
                return (first + last) >> 1;

            uint cprefix = clz((int)(firstCode ^ lastCode));
            uint split = first;
            uint step = last - first;

            do
            {
                step = (step + 1) >> 1;
                uint newSplit = split + step;
                if (newSplit < last)
                {
                    uint splitCode = mortonCodes[newSplit];
                    uint sprefix = clz((int)(firstCode ^ splitCode));
                    if (sprefix > cprefix)
                        split = newSplit;
                }
            } while (step > 1);

            return split;
        }

        private uint processRange(uint[] mortonCodes, LeafData[] leafData, uint first, uint last)
        {
            uint i;
            LBVH.Node node = new LBVH.Node();
            if (first == last)
            {
                Vector3D min = leafData[first].bounds.Min;
                Vector3D max = leafData[first].bounds.Max;
                node.min = new Vector3((float)min.x, (float)min.y, (float)min.z);
                node.max = new Vector3((float)max.x, (float)max.y, (float)max.z);
                node.childA = leafData[first].entity.Type;
                node.childB = leafData[first].index;
                i = first + bvh.LeafOffset;
                bvh.Nodes[i] = node;
                return i;
            }

            uint split = findSplit(mortonCodes, first, last);

            uint ia = processRange(mortonCodes, leafData, first, split);
            uint ib = processRange(mortonCodes, leafData, split + 1, last);

            i = bvh.GetNextInternalNodeIndex();

            LBVH.Node a = bvh.Nodes[ia];
            LBVH.Node b = bvh.Nodes[ib];
            node.min = new Vector3(
                Math.Min(a.min.X,b.min.X),
                Math.Min(a.min.Y,b.min.Y),
                Math.Min(a.min.Z,b.min.Z));
            node.max = new Vector3(
                Math.Max(a.max.X, b.max.X),
                Math.Max(a.max.Y, b.max.Y),
                Math.Max(a.max.Z, b.max.Z));
            node.childA = (int)ia;
            node.childB = (int)ib;
            bvh.Nodes[i] = node;

            return i;
        }


        public LBVH.Node[] Build(List<IEntity> ents)
        {
            if (ents.Count == 0 || ents[0] == null) return null;

            bvh = new LBVH((uint)ents.Count);

            //nodes = new BVHNode[nodeCount];
            leafData = new LeafData[bvh.LeafCount];
            mortonCodes = new uint[bvh.LeafCount];

            // compute global AABB
            AABB globalBounds = new AABB();
            for (int i = 0; i < bvh.LeafCount; ++i)
            {
                AABB tb = AABB.Transform(ents[i].Bounds, ents[i].Transform);
                leafData[i] = new LeafData(ents[i], i, tb);
                if (i == 0)
                {
                    globalBounds = tb;
                }
                else
                {
                    globalBounds.AssignUnion(tb);
                }
            }

            // compute morton index for each leaf
            for (int i = 0; i < bvh.LeafCount; ++i)
            {
                mortonCodes[i] = getMortonCode(
                    globalBounds.NormalizePoint(leafData[i].bounds.Mean));
            }

            // sort leaf data by morton index
            Array.Sort(mortonCodes, leafData);

            processRange(mortonCodes, leafData, 0, bvh.LeafCount - 1);

            return bvh.Nodes;
        }

        public Matrix[] GetTransformsForLastBuild()
        {
            int leafCount = (int)bvh.LeafCount;
            Matrix[] data = new Matrix[leafCount*2];

            for (int i = 0; i < leafCount; ++i)
            {
                Matrix T = Matrix.Transpose(leafData[i].entity.Transform.ToSlimDXMatrix());
                Matrix Ti = Matrix.Invert(T);
                data[i * 2] = T;
                data[i * 2 + 1] = Ti;
            }

            return data;
        }
    }
}
