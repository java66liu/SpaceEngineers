﻿using Sandbox.Common.Components;
using Sandbox.Engine.Utils;
using Sandbox.Engine.Voxels;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Diagnostics;
using VRage;
using VRage.Voxels;
using VRageMath;
using VRageRender;

namespace Sandbox.Game.Components
{
    class MyRenderComponentVoxelMap : MyRenderComponent
    {
        private IMyVoxelDrawable m_voxelMap = null;

        private readonly MyWorkTracker<UInt64, MyPrecalcJobRender> m_renderWorkTracker = new MyWorkTracker<UInt64, MyPrecalcJobRender>();

        public uint ClipmapId
        {
            get { return m_renderObjectIDs[0]; }
        }

        public override void OnAddedToContainer(MyComponentContainer container)
        {
            base.OnAddedToContainer(container);
            m_voxelMap = Entity as IMyVoxelDrawable;
        }

        public override void AddRenderObjects()
        {
            var minCorner = (Vector3D)(Entity as IMyVoxelDrawable).PositionLeftBottomCorner;
            m_renderObjectIDs = new uint[] { MyRenderProxy.RENDER_ID_UNASSIGNED };

            Debug.Assert((m_voxelMap.Size % MyVoxelConstants.RENDER_CELL_SIZE_IN_VOXELS) == Vector3I.Zero);
            var clipmapSizeLod0 = m_voxelMap.Size / MyVoxelConstants.RENDER_CELL_SIZE_IN_VOXELS;

            SetRenderObjectID(0,
                MyRenderProxy.CreateClipmap(
                    MatrixD.CreateTranslation(minCorner),
                    clipmapSizeLod0,
                    m_voxelMap.ScaleGroup,
                    Vector3D.Zero));
        }

        public override void InvalidateRenderObjects(bool sortIntoCulling = false)
        {
            if (Visible)
            {
                var worldMatrix = MatrixD.CreateTranslation(m_voxelMap.PositionLeftBottomCorner);
                MyRenderProxy.UpdateRenderObject(m_renderObjectIDs[0], ref worldMatrix, sortIntoCulling);
            }
        }

        public void UpdateCells()
        {
            var worldMatrix = MatrixD.CreateTranslation((Entity as IMyVoxelDrawable).PositionLeftBottomCorner);
            MyRenderProxy.UpdateRenderObject(m_renderObjectIDs[0], ref worldMatrix, sortIntoCulling: false);
        }

        public void InvalidateRange(Vector3I minVoxelChanged, Vector3I maxVoxelChanged)
        {
            minVoxelChanged -= MyPrecalcComponent.InvalidatedRangeInflate + 1;
            maxVoxelChanged += MyPrecalcComponent.InvalidatedRangeInflate + 1;
            m_voxelMap.Storage.ClampVoxelCoord(ref minVoxelChanged);
            m_voxelMap.Storage.ClampVoxelCoord(ref maxVoxelChanged);

            Vector3I minCellLod0, maxCellLod0;
            minVoxelChanged -= m_voxelMap.StorageMin;
            maxVoxelChanged -= m_voxelMap.StorageMin;

            MyVoxelCoordSystems.VoxelCoordToRenderCellCoord(ref minVoxelChanged, out minCellLod0);
            MyVoxelCoordSystems.VoxelCoordToRenderCellCoord(ref maxVoxelChanged, out maxCellLod0);

            MyRenderProxy.InvalidateClipmapRange(m_renderObjectIDs[0], minCellLod0, maxCellLod0);

            if (minCellLod0 == Vector3I.Zero &&
                maxCellLod0 == ((m_voxelMap.Storage.Geometry.CellsCount - 1) >> MyVoxelConstants.RENDER_CELL_SIZE_IN_GEOMETRY_CELLS_BITS))
            {
                m_renderWorkTracker.InvalidateAll();
            }
            else
            {
                for (int i = 0; i < MyCellCoord.MAX_LOD_COUNT; ++i)
                {
                    var minCell = minCellLod0 >> i;
                    var maxCell = maxCellLod0 >> i;
                    var cellCoord = new MyCellCoord(i, ref minCell);
                    for (var it = new Vector3I.RangeIterator(ref minCell, ref maxCell);
                        it.IsValid(); it.GetNext(out cellCoord.CoordInLod))
                    {
                        m_renderWorkTracker.Invalidate(cellCoord.PackId64());
                    }
                }
            }
        }

        internal void OnCellRequest(MyCellCoord cell, bool highPriority)
        {
            ProfilerShort.Begin("OnCellRequest");

            try
            {
                var workId = cell.PackId64();
                MyPrecalcJobRender job;
                if (m_renderWorkTracker.TryGet(workId, out job))
                {
                    if (!highPriority)
                    { // low priority work, no need to do anything
                        return;
                    }

                    if (job.IsHighPriority)
                    { // both are high priorities, so just invalidate previous one
                        m_renderWorkTracker.Invalidate(workId);
                        return;
                    }

                    // high priority arrived while there was one with low priority ... just cancel lower one
                    m_renderWorkTracker.Cancel(workId);
                }

                MyPrecalcJobRender.Start(new MyPrecalcJobRender.Args()
                {
                    Storage = m_voxelMap.Storage,
                    ClipmapId = ClipmapId,
                    Cell = cell,
                    WorkId = workId,
                    RenderWorkTracker = m_renderWorkTracker,
                    IsHighPriority = highPriority,
                });
            }
            finally
            {
                ProfilerShort.End();
            }
        }

        internal void OnCellRequestCancelled(MyCellCoord cell)
        {
            var workId = cell.PackId64();
            m_renderWorkTracker.Cancel(workId);
        }

        internal void CancelAllRequests()
        {
            m_renderWorkTracker.CancelAll();
        }
    }
}
