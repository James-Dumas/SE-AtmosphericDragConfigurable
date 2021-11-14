using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;


namespace dev.jamac.AtmosphericDragConfigurable
{
    public class Grid
    {
        public IMyEntity entity;
        public MyCubeGrid grid;
        public IMyCubeGrid iGrid;

        private int countForwardBackward = 0;
        private int countLeftRight = 0;
        private int countUpDown = 0;

        private List<IMySlimBlock> forwardSurfaceBlocks = new List<IMySlimBlock>();
        private List<IMySlimBlock> backwardSurfaceBlocks = new List<IMySlimBlock>();
        private List<IMySlimBlock> upSurfaceBlocks = new List<IMySlimBlock>();
        private List<IMySlimBlock> downSurfaceBlocks = new List<IMySlimBlock>();
        private List<IMySlimBlock> leftSurfaceBlocks = new List<IMySlimBlock>();
        private List<IMySlimBlock> rightSurfaceBlocks = new List<IMySlimBlock>();

        // update variables
        private int updateAtmosphereSkip = 0;
        private float atmosphere = 0;
        private float reEntryAtmosphere = 0;
        private int atmospheres = 0;

        int BlockCount;

        public Grid(IMyEntity entity)
        {
            this.entity = entity;
            grid = entity as MyCubeGrid;
            iGrid = grid as IMyCubeGrid;

            if (grid == null)
                return;

            iGrid.OnClosing += closeGrid;
            BlockCount = 0;
        }

        public void calculateAndApplyDrag()
        {
            float velocity = grid.Physics.LinearVelocity.Length();
            if (velocity > 20f)
            {
                // If blocks have been added or removed since last check need to update surfaces
                if (grid.BlocksCount != BlockCount)
                    updateSurfaceArea();

                // Location of grid
                Vector3 gridCenter = grid.Physics.CenterOfMassWorld;

                // Update atmosphere density once per second
                if (--updateAtmosphereSkip < 0)
                {
                    updateAtmosphereSkip = AtmosphericDrag.SKIP_TICKS_60;
                    atmosphere = 0;
                    reEntryAtmosphere = 0;
                    atmospheres = 0;

                    // Determine if entity is in atmosphere of any of the planets
                    foreach (var kv in AtmosphericDrag.planets)
                    {
                        var planet = kv.Value;
                        if (planet.Closed || planet.MarkedForClose)
                        {
                            continue;
                        }
                        // 3D Pythagorean Theorem "To get rid of that relatively CPU intensive SQRT part, multiply the distance you want to check against by itself."
                        if (planet.HasAtmosphere && Vector3D.DistanceSquared(gridCenter, planet.WorldMatrix.Translation) < (planet.AtmosphereRadius * planet.AtmosphereRadius))
                        {
                            float atmo = planet.GetAirDensity(gridCenter);
                            atmosphere += atmo;
                            reEntryAtmosphere += atmo;

                            atmospheres++;
                        }
                    }

                    // get atmospheric percentage (adjusted so altitudes between min and max chunks can clamp to 0 or 100%)
                    if (atmospheres > 0)
                    {
                        atmosphere /= atmospheres;
                        atmosphere = MathHelper.Clamp((atmosphere - AtmosphericDrag.MIN_ATMOSPHERE) / (AtmosphericDrag.MAX_ATMOSPHERE - AtmosphericDrag.MIN_ATMOSPHERE), 0f, 1f);
                        atmosphere = (float)Math.Pow(atmosphere, 3);
                    }
                }

                if (atmosphere <= 0)
                    return;

                // Vector of direction of movement
                Vector3 forward = Vector3.Normalize(grid.Physics.LinearVelocity);

                float speedSq = velocity * velocity;

                // If large grid apply multiplier to account for larger blocks and more drag (Largegrid returns 2.5f meters SmallGrid returns 0.5f meters)
                float gridSize = grid.GridSize;
                if (grid.GridSizeEnum == MyCubeSize.Large)
                    gridSize = gridSize * gridSize;

                // Velocities in 3 directions
                Vector3D shipForward = grid.WorldMatrix.Forward;
                Vector3D shipLeft = grid.WorldMatrix.Left;
                Vector3D shipUp = grid.WorldMatrix.Up;

                // Drag Multipliers
                double dragMultiplierFB = 0;
                double dragMultiplierLR = 0;
                double dragMultiplierUD = 0;

                // Calculate drag multiplier for the forward/backward side
                double dotForward = Vector3D.Dot(shipForward, forward);
                if (dotForward > 0)
                    dragMultiplierFB += dotForward * countForwardBackward;
                else
                    dragMultiplierFB += dotForward * countForwardBackward;

                // Calculate drag multiplier for the left/right side
                double dotLeft = Vector3D.Dot(shipLeft, forward);
                if (dotLeft > 0)
                    dragMultiplierLR += dotLeft * countLeftRight;
                else
                    dragMultiplierLR += dotLeft * countLeftRight;

                // Calculate drag multiplier for the up/down side
                double dotUp = Vector3D.Dot(shipUp, forward);
                if (dotUp > 0)
                    dragMultiplierUD += dotUp * countUpDown;
                else
                    dragMultiplierUD += dotUp * countUpDown;

                // Apply drag forces to grid
                grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -shipForward * AtmosphericDrag.DRAG_MULTIPLIER_INTERNAL * AtmosphericDrag.Instance.dragMultiplier * gridSize * (float)dragMultiplierFB * atmosphere * speedSq, gridCenter, null);
                grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -shipLeft * AtmosphericDrag.DRAG_MULTIPLIER_INTERNAL * AtmosphericDrag.Instance.dragMultiplier * gridSize * (float)dragMultiplierLR * atmosphere * speedSq, gridCenter, null);
                grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -shipUp * AtmosphericDrag.DRAG_MULTIPLIER_INTERNAL * AtmosphericDrag.Instance.dragMultiplier * gridSize * (float)dragMultiplierUD * atmosphere * speedSq, gridCenter, null);
            }
        }

        // updateSurfaceArea Method: Updates surface area
        private void updateSurfaceArea()
        {
            forwardSurfaceBlocks.Clear();
            backwardSurfaceBlocks.Clear();
            upSurfaceBlocks.Clear();
            downSurfaceBlocks.Clear();
            leftSurfaceBlocks.Clear();
            rightSurfaceBlocks.Clear();

            Vector3D Max = grid.Max;
            Vector3D Min = grid.Min;

            countForwardBackward = 0;
            countLeftRight = 0;
            countUpDown = 0;

            bool blockHit = false;

            //Calculate Forward Backward Surface Area
            for (int i = (int)Min.X; i <= Max.X; i++)
            {
                for (int j = (int)Min.Y; j <= Max.Y; j++)
                {
                    blockHit = false;
                    for (int k = (int)Min.Z; k <= Max.Z && !blockHit; k++)
                    {
                        IMySlimBlock tempBlock = grid.GetCubeBlock(new Vector3I(i, j, k));
                        if (tempBlock != null)
                        {
                            blockHit = true;
                            countForwardBackward++;
                            forwardSurfaceBlocks.Add(tempBlock);
                            break;
                        }
                    }
                    /* TODO: FOR COMPRESSION HEATING
                    if (blockHit)
                    {
                        for (int k = (int)Max.Z; k >= Min.Z; k--)
                        {
                            IMySlimBlock tempBlock = grid.GetCubeBlock(new Vector3I(i, j, k));
                            if (tempBlock != null)
                            {
                                backwardSurfaceBlocks.Add(tempBlock);
                                break;
                            }
                        }
                    }
                    */
                }
            }

            //Calculate Up Down Surface Area
            for (int i = (int)Min.X; i <= Max.X; i++)
            {
                for (int j = (int)Min.Z; j <= Max.Z; j++)
                {
                    blockHit = false;
                    for (int k = (int)Min.Y; k <= Max.Y && !blockHit; k++)
                    {
                        IMySlimBlock tempBlock = grid.GetCubeBlock(new Vector3I(i, k, j));
                        if (tempBlock != null)
                        {
                            blockHit = true;
                            countUpDown++;
                            upSurfaceBlocks.Add(tempBlock);
                            break;
                        }
                    }
                    /* TODO: FOR COMPRESSION HEATING
                    if (blockHit)
                    {
                        for (int k = (int)Max.Y; k >= Min.Y; k--)
                        {
                            IMySlimBlock tempBlock = grid.GetCubeBlock(new Vector3I(i, k, j));
                            if (tempBlock != null)
                            {
                                downSurfaceBlocks.Add(tempBlock);
                                break;
                            }
                        }
                    }
                    */
                }
            }

            //Calculate Left Right Surface Area
            for (int i = (int)Min.Y; i <= Max.Y; i++)
            {
                for (int j = (int)Min.Z; j <= Max.Z; j++)
                {
                    blockHit = false;
                    for (int k = (int)Min.X; k <= Max.X && !blockHit; k++)
                    {
                        IMySlimBlock tempBlock = grid.GetCubeBlock(new Vector3I(k, i, j));
                        if (tempBlock != null)
                        {
                            blockHit = true;
                            countLeftRight++;
                            leftSurfaceBlocks.Add(tempBlock);
                            break;
                        }
                    }
                    /* TODO: FOR COMPRESSION HEATING
                    if (blockHit)
                    {
                        for (int k = (int)Max.X; k >= Min.X; k--)
                        {
                            IMySlimBlock tempBlock = grid.GetCubeBlock(new Vector3I(k, i, j));
                            if (tempBlock != null)
                            {
                                rightSurfaceBlocks.Add(tempBlock);
                                break;
                            }
                        }
                    }
                    */
                }
            }
            BlockCount = grid.BlocksCount;
        }

        private void closeGrid(IMyEntity obj)
        {
            iGrid.OnClosing -= closeGrid;

            grid = null;
            iGrid = null;
            forwardSurfaceBlocks.Clear();
            backwardSurfaceBlocks.Clear();
            upSurfaceBlocks.Clear();
            downSurfaceBlocks.Clear();
            leftSurfaceBlocks.Clear();
            rightSurfaceBlocks.Clear();
        }
    }
}
