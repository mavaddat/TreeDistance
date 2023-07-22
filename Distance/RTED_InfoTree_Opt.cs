//    Copyright (C) 2012  Mateusz Pawlik and Nikolaus Augsten
//
//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU Affero General Public License as
//    published by the Free Software Foundation, either version 3 of the
//    License, or (at your option) any later version.
//
//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU Affero General Public License for more details.
//
//    You should have received a copy of the GNU Affero General Public License
//    along with this program.  If not, see <http://www.gnu.org/licenses/>.

using Barbar.TreeDistance.Util;
using System;
using System.Collections.Generic;

namespace Barbar.TreeDistance.Distance
{

    /**
     * Computes the tree edit distance using RTED algorithm.
     * 
     * @author Mateusz Pawlik, Nikolaus Augsten
     */
    public class RTED_InfoTree_Opt {

        // constants
        private const byte LEFT = 0;
        private const byte RIGHT = 1;
        private const byte HEAVY = 2;
        private const byte BOTH = 3;
        private const byte REVLEFT = 4;
        private const byte REVRIGHT = 5;
        private const byte REVHEAVY = 6;
        private const byte POST2_SIZE = 0;
        private const byte POST2_KR_SUM = 1;
        private const byte POST2_REV_KR_SUM = 2;
        private const byte POST2_DESC_SUM = 3; // number of subforests in
                                                      // full decomposition
        private const byte POST2_PRE = 4;
        private const byte POST2_PARENT = 5;
        private const byte POST2_LABEL = 6;
        private const byte KR = 7; // key root nodes (size of this array =
                                          // leaf count)
        private const byte POST2_LLD = 8; // left-most leaf descendants
        private const byte POST2_MIN_KR = 9; // minimum key root nodes index
                                                    // in KR array
        private const byte RKR = 10; // reversed key root nodes
        private const byte RPOST2_RLD = 11; // reversed postorer 2 right-most
                                                   // leaf descendants
        private const byte RPOST2_MIN_RKR = 12; // minimum key root nodes
                                                       // index in RKR array
        private const byte RPOST2_POST = 13; // reversed postorder ->
                                                    // postorder
        private const byte POST2_STRATEGY = 14; // strategy for Demaine (is
                                                       // there sth on the
                                                       // left/right of the heavy
                                                       // node)
        private const byte PRE2_POST = 15; // preorder to postorder

        // trees
        private InfoTree it1;
        private InfoTree it2;
        private int size1;
        private int size2;
        private LabelDictionary ld;

        // arrays
        private int[][] STR; // strategy array
        private double[][] delta; // an array for storing the distances between
                                  // every pair of subtrees
        private byte[][] deltaBit; // stores the distances difference of a form
                                   // delta(F,G)-delta(F°,G°) for every pair of
                                   // subtrees, which is at most 1
        private int[][] IJ; // stores a forest preorder for given i and j
        private long[][][] costV;
        private long[][] costW;
        private double[][] t; // T array from Demaine's algorithm, stores
                              // delta(Fv,Gij), v on heavy path. Values are
                              // written to t.
        private double[][] tCOPY; // tCOPY serves for writing values. It may happen
                                  // that in single computePeriod values are
                                  // overwritten before they are read because of
                                  // the change of forest ordering.
        private double[][] tTMP;
        private double[][] s;
        private double[] q;

        public long counter = 0; // stores the number of relevant subproblems
        private double da, db, dc;
        private int previousStrategy;
        public int[] strStat = new int[5]; // statistics for strategies
                                           // LEFT,RIGHT,HEAVY,SUM
        private double costDel, costIns, costMatch; // edit operations costs

        /**
         * The constructor. Parameters passed are the edit operation costs.
         * 
         * @param delCost
         * @param insCost
         * @param matchCost
         */
        public RTED_InfoTree_Opt(double delCost, double insCost, double matchCost) {
            this.costDel = delCost;
            this.costIns = insCost;
            this.costMatch = matchCost;
        }

        /**
         * Computes the tree edit distance between trees t1 and t2.
         * 
         * @param t1
         * @param t2
         * @return tree edit distance between trees t1 and t2
         */
        public double NonNormalizedTreeDist(LblTree t1, LblTree t2) {
            Init(t1, t2);
            STR = Arrays.Allocate<int>(size1, size2);
            ComputeOptimalStrategy();
            InitDelta();
            return ComputeDistUsingStrArray(it1, it2);
        }

        public double NonNormalizedTreeDist() {
            if (it1 == null || it2 == null) {
                Console.Error.WriteLine("No stored trees to compare.");
            }
            if (STR == null) {
                Console.Error.WriteLine("No strategy to use.");
            }
            InitDelta();
            return ComputeDistUsingStrArray(it1, it2);
        }

        /**
         * Initialization method.
         * 
         * @param t1
         * @param t2
         */
        public void Init(LblTree t1, LblTree t2) {
            ld = new LabelDictionary();
            it1 = new InfoTree(t1, ld);
            it2 = new InfoTree(t2, ld);
            size1 = it1.GetSize();
            size2 = it2.GetSize();
            IJ = Arrays.Allocate<int>(Math.Max(size1, size2), Math.Max(size1, size2));
            delta = Arrays.Allocate<double>(size1, size2);
            deltaBit = Arrays.Allocate<byte>(size1, size2);
            costV = Arrays.Allocate<long>(3, size1, size2);
            costW = Arrays.Allocate<long>(3, size2);
        }

        private void InitDelta() {
            // must be split from init because init was called before setting custom costs

            // Calculate delta between every leaf in G (empty tree) and all the
            // nodes in F.
            // Calculate it both sides: leafs of F and nodes of G & leafs of G and
            // nodes of F.
            int[] labels1 = it1.GetInfoArray(POST2_LABEL);
            int[] labels2 = it2.GetInfoArray(POST2_LABEL);
            int[] sizes1 = it1.GetInfoArray(POST2_SIZE);
            int[] sizes2 = it2.GetInfoArray(POST2_SIZE);
            for (int x = 0; x < sizes1.Length; x++) { // for all nodes of initially
                                                      // left tree
                for (int y = 0; y < sizes2.Length; y++) { // for all nodes of
                                                          // initially right tree

                    // This is an attempt for distances of single-node subtree and
                    // anything alse
                    // The differences between pairs of labels are stored
                    if (labels1[x] == labels2[y]) {
                        deltaBit[x][y] = 0;
                    } else {
                        deltaBit[x][y] = 1; // if this set, the labels differ, cost
                                            // of relabeling is set to costMatch
                    }

                    if (sizes1[x] == 1 && sizes2[y] == 1) { // both nodes are leafs
                        delta[x][y] = 0;
                    } else {
                        if (sizes1[x] == 1) {
                            delta[x][y] = (sizes2[y] - 1) * costIns;
                        }
                        if (sizes2[y] == 1) {
                            delta[x][y] = (sizes1[x] - 1) * costDel;
                        }
                    }
                }
            }
        }

        /**
         * A method for computing and storing the optimal strategy
         */
        public void ComputeOptimalStrategy() {
            long heavyMin, revHeavyMin, leftMin, revLeftMin, rightMin, revRightMin;
            long min = -1;
            int strategy = -1;
            int parent1 = -1;
            int parent2 = -1;
            bool[] nodeTypeLeft1 = it1.nodeType[LEFT];
            bool[] nodeTypeLeft2 = it2.nodeType[LEFT];
            bool[] nodeTypeRigt1 = it1.nodeType[RIGHT];
            bool[] nodeTypeRight2 = it2.nodeType[RIGHT];
            bool[] nodeTypeHeavy1 = it1.nodeType[HEAVY];
            bool[] nodeTypeHeavy2 = it2.nodeType[HEAVY];
            int[] post2size1 = it1.info[POST2_SIZE];
            int[] post2size2 = it2.info[POST2_SIZE];
            int[] post2descSum1 = it1.info[POST2_DESC_SUM];
            int[] post2descSum2 = it2.info[POST2_DESC_SUM];
            int[] post2krSum1 = it1.info[POST2_KR_SUM];
            int[] post2krSum2 = it2.info[POST2_KR_SUM];
            int[] post2revkrSum1 = it1.info[POST2_REV_KR_SUM];
            int[] post2revkrSum2 = it2.info[POST2_REV_KR_SUM];
            int[] post2parent1 = it1.info[POST2_PARENT];
            int[] post2parent2 = it2.info[POST2_PARENT];

            STR = Arrays.Allocate<int>(size1, size2);

            // v represents nodes of left input tree in postorder
            // w represents nodes of right input tree in postorder
            for (int v = 0; v < size1; v++) {
                Arrays.Fill(costW[0], 0);
                Arrays.Fill(costW[1], 0);
                Arrays.Fill(costW[2], 0);
                for (int w = 0; w < size2; w++) {
                    if (post2size2[w] == 1) {
                        // put zeros into arrays
                        costW[LEFT][w] = 0;
                        costW[RIGHT][w] = 0;
                        costW[HEAVY][w] = 0;
                    }
                    if (post2size1[v] == 1) {
                        // put zeros into arrays
                        costV[LEFT][v][w] = 0;
                        costV[RIGHT][v][w] = 0;
                        costV[HEAVY][v][w] = 0;
                    }

                    // TODO: some things below may be put to outer loop

                    // count the minimum + get the strategy
                    heavyMin = (long)post2size1[v] * (long)post2descSum2[w]
                        + costV[HEAVY][v][w];
                    revHeavyMin = (long)post2size2[w] * (long)post2descSum1[v]
                        + costW[HEAVY][w];

                    leftMin = (long)post2size1[v] * (long)post2krSum2[w]
                        + costV[LEFT][v][w];
                    revLeftMin = (long)post2size2[w] * (long)post2krSum1[v]
                        + costW[LEFT][w];

                    rightMin = (long)post2size1[v] * (long)post2revkrSum2[w]
                        + costV[RIGHT][v][w];
                    revRightMin = (long)post2size2[w] * (long)post2revkrSum1[v]
                        + costW[RIGHT][w];

                    long[] mins = { leftMin, rightMin, heavyMin, long.MaxValue,
            revLeftMin, revRightMin, revHeavyMin };

                    min = leftMin;
                    strategy = 0;
                    for (int i = 1; i <= 6; i++) {
                        if (mins[i] < min) {
                            min = mins[i];
                            strategy = i;
                        }
                    }

                    // store the strategy for the minimal cost
                    STR[v][w] = strategy;

                    // fill the cost arrays
                    parent1 = post2parent1[v];
                    if (parent1 != -1) {
                        costV[HEAVY][parent1][w] += nodeTypeHeavy1[v] ? costV[HEAVY][v][w]
                            : min;
                        costV[RIGHT][parent1][w] += nodeTypeRigt1[v] ? costV[RIGHT][v][w]
                            : min;
                        costV[LEFT][parent1][w] += nodeTypeLeft1[v] ? costV[LEFT][v][w]
                            : min;
                    }
                    parent2 = post2parent2[w];
                    if (parent2 != -1) {
                        costW[HEAVY][parent2] += nodeTypeHeavy2[w] ? costW[HEAVY][w]
                            : min;
                        costW[LEFT][parent2] += nodeTypeLeft2[w] ? costW[LEFT][w]
                            : min;
                        costW[RIGHT][parent2] += nodeTypeRight2[w] ? costW[RIGHT][w]
                            : min;
                    }
                }
            }
        }

        /**
         * The recursion step according to the optimal strategy.
         * 
         * @param it1
         * @param it2
         * @return
         */
        private double ComputeDistUsingStrArray(InfoTree it1, InfoTree it2) {

            int postorder1 = it1.GetCurrentNode();
            int postorder2 = it2.GetCurrentNode();

            int stepStrategy = STR[postorder1][postorder2];

            int tmpPostorder;

            int[] stepPath;
            int[] stepRelSubtrees;
            List<int> heavyPath;
            switch (stepStrategy) {
                case LEFT:
                    tmpPostorder = postorder1;
                    stepPath = it1.GetPath(LEFT);
                    // go along the path
                    while (stepPath[postorder1] > -1) {
                        stepRelSubtrees = it1.GetNodeRelSubtrees(LEFT, postorder1);
                        if (stepRelSubtrees != null) {
                            // iterate over rel subtrees for a specific node on the path
                            foreach(var rs in stepRelSubtrees) {
                                it1.SetCurrentNode(rs);
                                // make the recursion
                                ComputeDistUsingStrArray(it1, it2);
                            }
                        }
                        postorder1 = stepPath[postorder1];
                    }
                    // set current node
                    it1.SetCurrentNode(tmpPostorder);
                    it1.SetSwitched(false);
                    it2.SetSwitched(false);
                    // count the distance using a single-path function
                    strStat[3]++;
                    strStat[LEFT]++;
                    return SpfL(it1, it2);
                case RIGHT:
                    tmpPostorder = postorder1;
                    stepPath = it1.GetPath(RIGHT);
                    while (stepPath[postorder1] > -1) {
                        stepRelSubtrees = it1.GetNodeRelSubtrees(RIGHT, postorder1);
                        if (stepRelSubtrees != null) {
                            foreach (var rs in stepRelSubtrees) {
                                it1.SetCurrentNode(rs);
                                ComputeDistUsingStrArray(it1, it2);
                            }
                        }
                        postorder1 = stepPath[postorder1];
                    }
                    it1.SetCurrentNode(tmpPostorder);
                    it1.SetSwitched(false);
                    it2.SetSwitched(false);
                    strStat[3]++;
                    strStat[RIGHT]++;
                    return SpfR(it1, it2);
                case HEAVY:
                    tmpPostorder = postorder1;
                    stepPath = it1.GetPath(HEAVY);
                    heavyPath = new List<int>();
                    heavyPath.Add(postorder1);
                    while (stepPath[postorder1] > -1) {
                        stepRelSubtrees = it1.GetNodeRelSubtrees(HEAVY, postorder1);
                        if (stepRelSubtrees != null) {
                            foreach (var rs in stepRelSubtrees) {
                                it1.SetCurrentNode(rs);
                                ComputeDistUsingStrArray(it1, it2);
                            }
                        }
                        postorder1 = stepPath[postorder1];
                        heavyPath.Add(postorder1);
                    }
                    it1.SetCurrentNode(tmpPostorder);
                    it1.SetSwitched(false);
                    it2.SetSwitched(false);
                    strStat[3]++;
                    strStat[HEAVY]++;
                    return SpfH(it1, it2, heavyPath.ToArray());
                case REVLEFT:
                    tmpPostorder = postorder2;
                    stepPath = it2.GetPath(LEFT);
                    while (stepPath[postorder2] > -1) {
                        stepRelSubtrees = it2.GetNodeRelSubtrees(LEFT, postorder2);
                        if (stepRelSubtrees != null) {
                            foreach (var rs in stepRelSubtrees) {
                                it2.SetCurrentNode(rs);
                                ComputeDistUsingStrArray(it1, it2);
                            }
                        }
                        postorder2 = stepPath[postorder2];
                    }
                    it2.SetCurrentNode(tmpPostorder);
                    it1.SetSwitched(true);
                    it2.SetSwitched(true);
                    strStat[3]++;
                    strStat[LEFT]++;
                    return SpfL(it2, it1);
                case REVRIGHT:
                    tmpPostorder = postorder2;
                    stepPath = it2.GetPath(RIGHT);
                    while (stepPath[postorder2] > -1) {
                        stepRelSubtrees = it2.GetNodeRelSubtrees(RIGHT, postorder2);
                        if (stepRelSubtrees != null) {
                            foreach (var rs in stepRelSubtrees) {
                                it2.SetCurrentNode(rs);
                                ComputeDistUsingStrArray(it1, it2);
                            }
                        }
                        postorder2 = stepPath[postorder2];
                    }
                    it2.SetCurrentNode(tmpPostorder);
                    it1.SetSwitched(true);
                    it2.SetSwitched(true);
                    strStat[3]++;
                    strStat[RIGHT]++;
                    return SpfR(it2, it1);
                case REVHEAVY:
                    tmpPostorder = postorder2;
                    stepPath = it2.GetPath(HEAVY);
                    heavyPath = new List<int>();
                    heavyPath.Add(postorder2);
                    while (stepPath[postorder2] > -1) {
                        stepRelSubtrees = it2.GetNodeRelSubtrees(HEAVY, postorder2);
                        if (stepRelSubtrees != null) {
                            foreach (var rs in stepRelSubtrees) {
                                it2.SetCurrentNode(rs);
                                ComputeDistUsingStrArray(it1, it2);
                            }
                        }
                        postorder2 = stepPath[postorder2];
                        heavyPath.Add(postorder2);
                    }
                    it2.SetCurrentNode(tmpPostorder);
                    it1.SetSwitched(true);
                    it2.SetSwitched(true);
                    strStat[3]++;
                    strStat[HEAVY]++;
                    return SpfH(it2, it1, heavyPath.ToArray());
                default:
                    return -1;
            }
        }

        /**
         * Single-path function for the left-most path based on Zhang and Shasha
         * algorithm.
         * 
         * @param it1
         * @param it2
         * @return distance between subtrees it1 and it2
         */
        private double SpfL(InfoTree it1, InfoTree it2) {

            int fPostorder = it1.GetCurrentNode();
            int gPostorder = it2.GetCurrentNode();

            int minKR = it2.info[POST2_MIN_KR][gPostorder];
            int[] kr = it2.info[KR];
            if (minKR > -1) {
                for (int j = minKR; kr[j] < gPostorder; j++) {
                    TreeEditDist(it1, it2, fPostorder, kr[j]);
                }
            }
            TreeEditDist(it1, it2, fPostorder, gPostorder);

            return it1.IsSwitched() ? delta[gPostorder][fPostorder]
                + deltaBit[gPostorder][fPostorder] * costMatch
                : delta[fPostorder][gPostorder]
                    + deltaBit[fPostorder][gPostorder] * costMatch;
        }

        private void TreeEditDist(InfoTree it1, InfoTree it2, int i, int j) {
            int m = i - it1.info[POST2_LLD][i] + 2;
            int n = j - it2.info[POST2_LLD][j] + 2;
            double[][] forestdist = Arrays.Allocate<double>(m, n);
            int ioff = it1.info[POST2_LLD][i] - 1;
            int joff = it2.info[POST2_LLD][j] - 1;
            bool switched = it1.IsSwitched();
            forestdist[0][0] = 0;
            for (int i1 = 1; i1 <= i - ioff; i1++) {
                forestdist[i1][0] = forestdist[i1 - 1][0] + costDel;
            }
            for (int j1 = 1; j1 <= j - joff; j1++) {
                forestdist[0][j1] = forestdist[0][j1 - 1] + costIns;
            }
            for (int i1 = 1; i1 <= i - ioff; i1++) {
                for (int j1 = 1; j1 <= j - joff; j1++) {
                    counter++;
                    if ((it1.info[POST2_LLD][i1 + ioff] == it1.info[POST2_LLD][i])
                        && (it2.info[POST2_LLD][j1 + joff] == it2.info[POST2_LLD][j])) {
                        double u = 0;
                        if (it1.info[POST2_LABEL][i1 + ioff] != it2.info[POST2_LABEL][j1
                            + joff]) {
                            u = costMatch;
                        }
                        da = forestdist[i1 - 1][j1] + costDel;
                        db = forestdist[i1][j1 - 1] + costIns;
                        dc = forestdist[i1 - 1][j1 - 1] + u;
                        forestdist[i1][j1] = (da < db) ? ((da < dc) ? da : dc)
                            : ((db < dc) ? db : dc);

                        SetDeltaValue(i1 + ioff, j1 + joff,
                            forestdist[i1 - 1][j1 - 1], switched);
                        SetDeltaBitValue(i1 + ioff, j1 + joff,
                            (byte)((forestdist[i1][j1]
                                - forestdist[i1 - 1][j1 - 1] > 0) ? 1 : 0),
                            switched);
                    } else {
                        double u = 0;
                        u = switched ? deltaBit[j1 + joff][i1 + ioff] * costMatch
                            : deltaBit[i1 + ioff][j1 + joff] * costMatch;

                        da = forestdist[i1 - 1][j1] + costDel;
                        db = forestdist[i1][j1 - 1] + costIns;
                        dc = forestdist[it1.info[POST2_LLD][i1 + ioff] - 1 - ioff][it2.info[POST2_LLD][j1
                            + joff]
                            - 1 - joff]
                            + (switched ? delta[j1 + joff][i1 + ioff]
                                : delta[i1 + ioff][j1 + joff]) + u;
                        forestdist[i1][j1] = (da < db) ? ((da < dc) ? da : dc)
                            : ((db < dc) ? db : dc);
                    }
                }
            }
        }

        /**
         * Single-path function for right-most path based on symmetric version of
         * Zhang and Shasha algorithm.
         * 
         * @param it1
         * @param it2
         * @return distance between subtrees it1 and it2
         */
        private double SpfR(InfoTree it1, InfoTree it2) {

            int fReversedPostorder = it1.GetSize() - 1
                - it1.info[POST2_PRE][it1.GetCurrentNode()];
            int gReversedPostorder = it2.GetSize() - 1
                - it2.info[POST2_PRE][it2.GetCurrentNode()];

            int minRKR = it2.info[RPOST2_MIN_RKR][gReversedPostorder];
            int[] rkr = it2.info[RKR];
            if (minRKR > -1) {
                for (int j = minRKR; rkr[j] < gReversedPostorder; j++) {
                    TreeEditDistRev(it1, it2, fReversedPostorder, rkr[j]);
                }
            }
            TreeEditDistRev(it1, it2, fReversedPostorder, gReversedPostorder);

            return it1.IsSwitched() ? delta[it2.GetCurrentNode()][it1
                .GetCurrentNode()]
                + deltaBit[it2.GetCurrentNode()][it1.GetCurrentNode()]
                * costMatch : delta[it1.GetCurrentNode()][it2.GetCurrentNode()]
                + deltaBit[it1.GetCurrentNode()][it2.GetCurrentNode()]
                * costMatch;
        }

        private void TreeEditDistRev(InfoTree it1, InfoTree it2, int i, int j) {
            int m = i - it1.info[RPOST2_RLD][i] + 2;
            int n = j - it2.info[RPOST2_RLD][j] + 2;
            double[][] forestdist = Arrays.Allocate<double>(m, n);
            int ioff = it1.info[RPOST2_RLD][i] - 1;
            int joff = it2.info[RPOST2_RLD][j] - 1;
            bool switched = it1.IsSwitched();
            forestdist[0][0] = 0;
            for (int i1 = 1; i1 <= i - ioff; i1++) {
                forestdist[i1][0] = forestdist[i1 - 1][0] + costDel;
            }
            for (int j1 = 1; j1 <= j - joff; j1++) {
                forestdist[0][j1] = forestdist[0][j1 - 1] + costIns;
            }
            for (int i1 = 1; i1 <= i - ioff; i1++) {
                for (int j1 = 1; j1 <= j - joff; j1++) {
                    counter++;
                    if ((it1.info[RPOST2_RLD][i1 + ioff] == it1.info[RPOST2_RLD][i])
                        && (it2.info[RPOST2_RLD][j1 + joff] == it2.info[RPOST2_RLD][j])) {
                        double u = 0;
                        if (it1.info[POST2_LABEL][it1.info[RPOST2_POST][i1 + ioff]] != it2.info[POST2_LABEL][it2.info[RPOST2_POST][j1
                            + joff]]) {
                            u = costMatch;
                        }
                        da = forestdist[i1 - 1][j1] + costDel;
                        db = forestdist[i1][j1 - 1] + costIns;
                        dc = forestdist[i1 - 1][j1 - 1] + u;
                        forestdist[i1][j1] = (da < db) ? ((da < dc) ? da : dc)
                            : ((db < dc) ? db : dc);

                        SetDeltaValue(it1.info[RPOST2_POST][i1 + ioff],
                            it2.info[RPOST2_POST][j1 + joff],
                            forestdist[i1 - 1][j1 - 1], switched);
                        SetDeltaBitValue(it1.info[RPOST2_POST][i1 + ioff],
                            it2.info[RPOST2_POST][j1 + joff],
                            (byte)((forestdist[i1][j1]
                                - forestdist[i1 - 1][j1 - 1] > 0) ? 1 : 0),
                            switched);
                    } else {
                        double u = 0;
                        u = switched ? deltaBit[it2.info[RPOST2_POST][j1 + joff]][it1.info[RPOST2_POST][i1
                            + ioff]]
                            * costMatch
                            : deltaBit[it1.info[RPOST2_POST][i1 + ioff]][it2.info[RPOST2_POST][j1
                                + joff]]
                                * costMatch;

                        da = forestdist[i1 - 1][j1] + costDel;
                        db = forestdist[i1][j1 - 1] + costIns;
                        dc = forestdist[it1.info[RPOST2_RLD][i1 + ioff] - 1 - ioff][it2.info[RPOST2_RLD][j1
                            + joff]
                            - 1 - joff]
                            + (switched ? delta[it2.info[RPOST2_POST][j1 + joff]][it1.info[RPOST2_POST][i1
                                + ioff]]
                                : delta[it1.info[RPOST2_POST][i1 + ioff]][it2.info[RPOST2_POST][j1
                                    + joff]]) + u;
                        forestdist[i1][j1] = (da < db) ? ((da < dc) ? da : dc)
                            : ((db < dc) ? db : dc);
                    }
                }
            }

        }

        /**
         * Single-path function for heavy path based on Klein/Demaine algorithm.
         * 
         * @param it1
         * @param it2
         * @param heavyPath
         * @return distance between subtrees it1 and it2
         */
        private double SpfH(InfoTree it1, InfoTree it2, int[] heavyPath) {

            int fSize = it1.info[POST2_SIZE][it1.GetCurrentNode()];
            int gSize = it2.info[POST2_SIZE][it2.GetCurrentNode()];

            int gRevPre = it2.GetSize() - 1 - it2.GetCurrentNode();
            int gPre = it2.info[POST2_PRE][it2.GetCurrentNode()];

            int gTreeSize = it2.GetSize();

            int strategy;

            int jOfi;

            // Initialize arrays to their maximal possible size for current pairs of
            // subtrees.
            t = Arrays.Allocate<double>(gSize, gSize);
            tCOPY = Arrays.Allocate<double>(gSize, gSize);
            s = Arrays.Allocate<double>(fSize, gSize);
            q = new double[fSize];

            int vp = -1;
            int nextVp = -1;

            for (int it = heavyPath.Length - 1; it >= 0; it--) {
                vp = heavyPath[it];
                strategy = it1.info[POST2_STRATEGY][vp];
                if (strategy != BOTH) {
                    if (it1.info[POST2_SIZE][vp] == 1) {
                        for (int i = gSize - 1; i >= 0; i--) {
                            jOfi = JOfI(it2, i, gSize, gRevPre, gPre, strategy,
                                gTreeSize);
                            for (int j = jOfi; j >= 0; j--) {
                                t[i][j] = (gSize - (i + j)) * costIns;
                            }
                        }
                        previousStrategy = strategy;
                    }
                    ComputePeriod(it1, vp, nextVp, it2, strategy);
                } else {
                    if (it1.info[POST2_SIZE][vp] == 1) {
                        for (int i = gSize - 1; i >= 0; i--) {
                            jOfi = JOfI(it2, i, gSize, gRevPre, gPre, LEFT,
                                gTreeSize);
                            for (int j = jOfi; j >= 0; j--) {
                                t[i][j] = (gSize - (i + j)) * costIns;
                            }
                        }
                        previousStrategy = LEFT;
                    }
                    ComputePeriod(it1, vp, nextVp, it2, LEFT);
                    if (it1.info[POST2_SIZE][vp] == 1) {
                        for (int i = gSize - 1; i >= 0; i--) {
                            jOfi = JOfI(it2, i, gSize, gRevPre, gPre, RIGHT,
                                gTreeSize);
                            for (int j = jOfi; j >= 0; j--) {
                                t[i][j] = (gSize - (i + j)) * costIns;
                            }
                        }
                        previousStrategy = RIGHT;
                    }
                    ComputePeriod(it1, vp, nextVp, it2, RIGHT);
                }
                nextVp = vp;
            }
            return t[0][0];
        }

        /**
         * Compute period method.
         * 
         * @param it1
         * @param aVp
         * @param aNextVp
         * @param it2
         * @param aStrategy
         */
        private void ComputePeriod(InfoTree it1, int aVp, int aNextVp,
            InfoTree it2, int aStrategy) {

            int fTreeSize = it1.GetSize();
            int gTreeSize = it2.GetSize();

            int vpPreorder = it1.info[POST2_PRE][aVp];
            int vpRevPreorder = fTreeSize - 1 - aVp;
            int vpSize = it1.info[POST2_SIZE][aVp];

            int gSize = it2.info[POST2_SIZE][it2.GetCurrentNode()];
            int gPreorder = it2.info[POST2_PRE][it2.GetCurrentNode()];
            int gRevPreorder = gTreeSize - 1 - it2.GetCurrentNode();

            int nextVpPreorder = -1;
            int nextVpRevPreorder = -1;
            int nextVpSize = -1;
            // count k and assign next vp values
            int k;
            if (aNextVp != -1) {
                nextVpPreorder = it1.info[POST2_PRE][aNextVp];
                nextVpRevPreorder = fTreeSize - 1 - aNextVp;
                nextVpSize = it1.info[POST2_SIZE][aNextVp];
                // if strategy==LEFT use preorder to count number of left deletions
                // from vp to vp-1
                // if strategy==RIGHT use reversed preorder
                k = aStrategy == LEFT ? nextVpPreorder - vpPreorder
                    : nextVpRevPreorder - vpRevPreorder;
                if (aStrategy != previousStrategy) {
                    ComputeIJTable(it2, gPreorder, gRevPreorder, gSize, aStrategy,
                        gTreeSize);
                }
            } else {
                k = 1;
                ComputeIJTable(it2, gPreorder, gRevPreorder, gSize, aStrategy,
                    gTreeSize);
            }

            int realStrategy = it1.info[POST2_STRATEGY][aVp];

            bool switched = it1.IsSwitched();

            tTMP = tCOPY;
            tCOPY = t;
            t = tTMP;

            // if aVp is a leaf => precompute table T - edit distance betwen EMPTY
            // and all subforests of G

            // check if nextVp is the only child of vp
            if (vpSize - nextVpSize == 1) {
                // update delta from T table => dist between Fvp-1 and G was
                // computed in previous compute period
                if (gSize == 1) {
                    SetDeltaValue(it1.info[PRE2_POST][vpPreorder],
                        it2.info[PRE2_POST][gPreorder], (vpSize - 1) * costDel, switched);
                } else {
                    SetDeltaValue(it1.info[PRE2_POST][vpPreorder],
                        it2.info[PRE2_POST][gPreorder], t[1][0], switched);
                }
            }

            int gijForestPreorder;
            int previousI;
            int fForestPreorderKPrime;
            int jPrime;
            int kBis;
            int jOfIminus1;
            int gijOfIMinus1Preorder;
            int jOfI;
            double deleteFromLeft;
            double deleteFromRight;
            double match;
            int fLabel;
            int gLabel;

            // Q and T are visible for every i
            for (int i = gSize - 1; i >= 0; i--) {

                // jOfI was already computed once in spfH
                jOfI = this.JOfI(it2, i, gSize, gRevPreorder, gPreorder, aStrategy,
                    gTreeSize);

                // when strategy==BOTH first LEFT then RIGHT is done

                counter += realStrategy == BOTH && aStrategy == LEFT ? (k - 1)
                    * (jOfI + 1) : k * (jOfI + 1);

                // S - visible for current i

                for (int kPrime = 1; kPrime <= k; kPrime++) {

                    fForestPreorderKPrime = aStrategy == LEFT ? vpPreorder
                        + (k - kPrime) : it1.info[POST2_PRE][fTreeSize - 1
                        - (vpRevPreorder + (k - kPrime))];
                    kBis = kPrime
                        - it1.info[POST2_SIZE][it1.info[PRE2_POST][fForestPreorderKPrime]];

                    // reset the minimum arguments' values
                    deleteFromRight = costIns;
                    deleteFromLeft = costDel;
                    match = 0;

                    match += aStrategy == LEFT ? (kBis + nextVpSize) * costIns : (vpSize - k + kBis) * costIns;

                    if ((i + jOfI) == (gSize - 1)) {
                        deleteFromRight += (vpSize - (k - kPrime)) * costIns;
                    } else {
                        deleteFromRight += q[kPrime - 1];
                    }

                    fLabel = it1.info[POST2_LABEL][it1.info[PRE2_POST][fForestPreorderKPrime]];

                    for (int j = jOfI; j >= 0; j--) {
                        // count dist(FkPrime, Gij) with min

                        // delete from left

                        gijForestPreorder = aStrategy == LEFT ? IJ[i][j]
                            : it2.info[POST2_PRE][gTreeSize - 1 - IJ[i][j]];

                        if (kPrime == 1) {
                            // if the direction changed from the previous period to
                            // this one use i and j of previous strategy

                            // since T is overwritten continuously, thus use copy of
                            // T for getting values from previous period

                            if (aStrategy != previousStrategy) {
                                if (aStrategy == LEFT) {
                                    previousI = gijForestPreorder - gPreorder; // minus
                                                                               // preorder
                                                                               // of
                                                                               // G
                                } else {
                                    previousI = gTreeSize
                                        - 1
                                        - it2.info[RPOST2_POST][gTreeSize - 1
                                            - gijForestPreorder]
                                        - gRevPreorder; // minus rev preorder of
                                                        // G
                                }
                                deleteFromLeft += tCOPY[previousI][i + j - previousI];
                            } else {
                                deleteFromLeft += tCOPY[i][j];
                            }

                        } else {
                            deleteFromLeft += s[kPrime - 1 - 1][j];
                        }

                        // match

                        match += switched ? delta[it2.info[PRE2_POST][gijForestPreorder]][it1.info[PRE2_POST][fForestPreorderKPrime]]
                            : delta[it1.info[PRE2_POST][fForestPreorderKPrime]][it2.info[PRE2_POST][gijForestPreorder]];

                        jPrime = j + it2.info[POST2_SIZE][it2.info[PRE2_POST][gijForestPreorder]];

                        // if root nodes of L/R Fk' and L/R Gij have different
                        // labels add the match cost
                        gLabel = it2.info[POST2_LABEL][it2.info[PRE2_POST][gijForestPreorder]];

                        if (fLabel != gLabel) {
                            match += costMatch;
                        }

                        // this condition is checked many times but is not satisfied
                        // only once
                        if (j != jOfI) {
                            // delete from right
                            deleteFromRight += s[kPrime - 1][j + 1];
                            if (kBis == 0) {
                                if (aStrategy != previousStrategy) {
                                    previousI = aStrategy == LEFT ? IJ[i][jPrime]
                                        - gPreorder : IJ[i][jPrime]
                                        - gRevPreorder;
                                    match += tCOPY[previousI][i + jPrime - previousI];
                                } else {
                                    match += tCOPY[i][jPrime];
                                }
                            } else if (kBis > 0) {
                                match += s[kBis - 1][jPrime];
                            } else {
                                match += (gSize - (i + jPrime)) * costIns;
                            }

                        }

                        // fill S table
                        s[kPrime - 1][j] = (deleteFromLeft < deleteFromRight) ? ((deleteFromLeft < match) ? deleteFromLeft
                            : match)
                            : ((deleteFromRight < match) ? deleteFromRight
                                : match);

                        // reset the minimum arguments' values
                        deleteFromRight = costIns;
                        deleteFromLeft = costDel;
                        match = 0;
                    }
                }

                // compute table T => add row to T
                // if (realStrategy == BOTH && aStrategy == LEFT) {
                // // t[i] has to be of correct length
                // // assigning pointer of a row in s to t[i] is wrong
                // // t[i] = Arrays.copyOf(s[k-1-1], jOfI+1);//sTable[k - 1 - 1];
                // // System.arraycopy(s[k-1-1], 0, t[i], 0, jOfI+1);
                // t[i] = s[k-1-1].clone();
                // } else {
                // // t[i] = Arrays.copyOf(s[k-1], jOfI+1);//sTable[k - 1];
                // // System.arraycopy(s[k-1], 0, t[i], 0, jOfI+1);
                // t[i] = s[k-1].clone();
                // }

                // compute table T => add row to T
                // we have to copy the values, otherwise they may be overwritten t
                // early
                double[] clone;
                if (realStrategy == BOTH && aStrategy == LEFT)
                {
                    clone = new double[s[k - 1 - 1].Length];
                    Array.Copy(s[k - 1 - 1], clone, s[k - 1 - 1].Length);
                }
                else
                {
                    clone = new double[s[k - 1].Length];
                    Array.Copy(s[k - 1], clone, s[k - 1].Length);
                }
                t[i] = clone;

                if (i > 0) {
                    // compute table Q
                    jOfIminus1 = this.JOfI(it2, i - 1, gSize, gRevPreorder, gPreorder,
                        aStrategy, gTreeSize);
                    if (jOfIminus1 <= jOfI) {
                        for (int x = 0; x < k; x++) { // copy whole column |
                                                      // qTable.length=k
                            q[x] = s[x][jOfIminus1];
                        }
                    }

                    // fill table delta
                    if (i + jOfIminus1 < gSize) {

                        gijOfIMinus1Preorder = aStrategy == LEFT ? it2.info[POST2_PRE][gTreeSize
                            - 1 - (gRevPreorder + (i - 1))]
                            : gPreorder + (i - 1);

                        // If Fk from Fk-1 differ with a single node,
                        // then Fk without the root node is Fk-1 and the distance
                        // value has to be taken from previous T table.
                        if (k - 1 - 1 < 0) {
                            if (aStrategy != previousStrategy) {
                                previousI = aStrategy == LEFT ? IJ[i][jOfIminus1]
                                    - gPreorder : IJ[i][jOfIminus1]
                                    - gRevPreorder;
                                SetDeltaValue(
                                    it1.info[PRE2_POST][vpPreorder],
                                    it2.info[PRE2_POST][gijOfIMinus1Preorder],
                                    tCOPY[previousI][i + jOfIminus1 - previousI],
                                    switched);
                            } else {
                                SetDeltaValue(it1.info[PRE2_POST][vpPreorder],
                                    it2.info[PRE2_POST][gijOfIMinus1Preorder],
                                    tCOPY[i][jOfIminus1], switched);
                            }
                        } else {
                            SetDeltaValue(it1.info[PRE2_POST][vpPreorder],
                                it2.info[PRE2_POST][gijOfIMinus1Preorder],
                                s[k - 1 - 1][jOfIminus1], switched);
                        }
                    }
                }

            }
            previousStrategy = aStrategy;
        }

        /**
         * Computes an array where preorder/rev.preorder of a subforest of given
         * subtree is stored and can be accessed for given i and j.
         * 
         * @param it
         * @param subtreePreorder
         * @param subtreeRevPreorder
         * @param subtreeSize
         * @param aStrategy
         * @param treeSize
         */
        private void ComputeIJTable(InfoTree it, int subtreePreorder,
            int subtreeRevPreorder, int subtreeSize, int aStrategy, int treeSize) {

            int change;

            int[] post2pre = it.info[POST2_PRE];
            int[] rpost2post = it.info[RPOST2_POST];

            if (aStrategy == LEFT) {
                for (int x = 0; x < subtreeSize; x++) {
                    IJ[0][x] = x + subtreePreorder;
                }
                for (int x = 1; x < subtreeSize; x++) {
                    change = post2pre[(treeSize - 1 - (x - 1 + subtreeRevPreorder))];
                    for (int z = 0; z < subtreeSize; z++) {
                        if (IJ[x - 1][z] >= change) {
                            IJ[x][z] = IJ[x - 1][z] + 1;
                        } else {
                            IJ[x][z] = IJ[x - 1][z];
                        }
                    }
                }
            } else {// if (aStrategy == RIGHT) {
                for (int x = 0; x < subtreeSize; x++) {
                    IJ[0][x] = x + subtreeRevPreorder;
                }
                for (int x = 1; x < subtreeSize; x++) {
                    change = treeSize
                        - 1
                        - rpost2post[(treeSize - 1 - (x - 1 + subtreePreorder))];
                    for (int z = 0; z < subtreeSize; z++) {
                        if (IJ[x - 1][z] >= change) {
                            IJ[x][z] = IJ[x - 1][z] + 1;
                        } else {
                            IJ[x][z] = IJ[x - 1][z];
                        }
                    }
                }
            }
        }

        /**
         * Returns j for given i, result of j(i) form Demaine's algorithm.
         * 
         * @param it
         * @param aI
         * @param aSubtreeWeight
         * @param aSubtreeRevPre
         * @param aSubtreePre
         * @param aStrategy
         * @param treeSize
         * @return j for given i
         */
        private int JOfI(InfoTree it, int aI, int aSubtreeWeight,
            int aSubtreeRevPre, int aSubtreePre, int aStrategy, int treeSize) {
            return aStrategy == LEFT ? aSubtreeWeight - aI
                - it.info[POST2_SIZE][treeSize - 1 - (aSubtreeRevPre + aI)]
                : aSubtreeWeight
                    - aI
                    - it.info[POST2_SIZE][it.info[RPOST2_POST][treeSize - 1
                        - (aSubtreePre + aI)]];
        }

        private void SetDeltaValue(int a, int b, double value, bool switched) {
            if (switched) {
                delta[b][a] = value;
            } else {
                delta[a][b] = value;
            }
        }

        private void SetDeltaBitValue(int a, int b, byte value, bool switched) {
            if (switched) {
                deltaBit[b][a] = value;
            } else {
                deltaBit[a][b] = value;
            }
        }

        public void SetCustomCosts(double costDel, double costIns, double costMatch) {
            this.costDel = costDel;
            this.costIns = costIns;
            this.costMatch = costMatch;
        }

        public void SetCustomStrategy(int[][] strategyArray) {
            STR = strategyArray;
        }

        public void SetCustomStrategy(int strategy, bool ifSwitch) {
            STR = Arrays.Allocate<int>(size1, size2);
            if (ifSwitch) {
                for (int i = 0; i < size1; i++) {
                    for (int j = 0; j < size2; j++) {
                        STR[i][j] = it1.info[POST2_SIZE][i] >= it2.info[POST2_SIZE][j] ? strategy
                            : strategy + 4;
                    }
                }
            } else {
                for (int i = 0; i < size1; i++) {
                    Arrays.Fill(STR[i], strategy);
                }
            }
        }

        /**
         * Compute the minimal edit mapping between two trees. There might be
         * multiple minimal edit mappings. This function computes only one of them.
         * 
         * The first step of this function is to compute the tree edit distance.
         * Based on the tree distance matrix the mapping is computed.
         * 
         * @return all pairs (ted1.node,ted2.node) of the minimal edit mapping. Each
         *         element in the collection is an integer array A of size 2, where
         *         A[0]=ted1.node is the postorderID (starting with 1) of the nodes
         *         in ted1 and A[1]=ted2.node is the postorderID in ted2. The
         *         postorderID of the empty node (insertion, deletion) is zero.
         */
        public Stack<int[]> ComputeEditMapping() {

            // initialize tree and forest distance arrays
            double[][] treedist = Arrays.Allocate<double>(size1 + 1, size2 + 1);
            double[][] forestdist = Arrays.Allocate<double>(size1 + 1, size2 + 1);

            bool rootNodePair = true;

            // treedist was already computed - the result is in delta and deltaBit
            for (int i = 0; i < size1; i++) {
                treedist[i][0] = i * costDel;
            }
            for (int j = 0; j < size2; j++) {
                treedist[0][j] = j * costIns;
            }
            for (int i = 1; i <= size1; i++) {
                for (int j = 1; j <= size2; j++) {
                    treedist[i][j] = delta[i - 1][j - 1] + deltaBit[i - 1][j - 1] * costMatch;
                }
            }

            // forestdist for input trees has to be computed
            ForestDist(it1, it2, size1, size2, treedist, forestdist);

            // empty edit mapping
            var editMapping = new Stack<int[]>();

            // empty stack of tree Pairs
            var treePairs = new Stack<int[]>();
            // push the pair of trees (ted1,ted2) to stack
            treePairs.Push(new int[] { size1, size2 });

            while (treePairs.Count > 0) {

                // get next tree pair to be processed
                int[] treePair = treePairs.Pop();
                int lastRow = treePair[0];
                int lastCol = treePair[1];

                // compute forest distance matrix
                if (!rootNodePair) {
                    ForestDist(it1, it2, lastRow, lastCol, treedist, forestdist);
                }
                rootNodePair = false;

                // compute mapping for current forest distance matrix
                int firstRow = it1.GetInfo(POST2_LLD, lastRow - 1) + 1 - 1;
                int firstCol = it2.GetInfo(POST2_LLD, lastCol - 1) + 1 - 1;
                int row = lastRow;
                int col = lastCol;
                while ((row > firstRow) || (col > firstCol)) {
                    if ((row > firstRow)
                        && (forestdist[row - 1][col] + costDel == forestdist[row][col])) {
                        // node with postorderID row is deleted from ted1
                        editMapping.Push(new int[] { row, 0 });
                        row--;
                    } else if ((col > firstCol)
                        && (forestdist[row][col - 1] + costIns == forestdist[row][col])) {
                        // node with postorderID col is inserted into ted2
                        editMapping.Push(new int[] { 0, col });
                        col--;
                    } else {
                        // node with postorderID row in ted1 is renamed to node col
                        // in ted2

                        if ((it1.GetInfo(POST2_LLD, row - 1) == it1.GetInfo(POST2_LLD, lastRow - 1))
                            && (it2.GetInfo(POST2_LLD, col - 1) == it2.GetInfo(POST2_LLD, lastCol - 1))) {
                            // if both subforests are trees, map nodes
                            editMapping.Push(new int[] { row, col });
                            row--;
                            col--;
                        } else {
                            // pop subtree pair
                            treePairs.Push(new int[] { row, col });

                            // continue with forest to the left of the popped
                            // subtree pair
                            row = it1.GetInfo(POST2_LLD, row - 1) + 1 - 1;
                            col = it2.GetInfo(POST2_LLD, col - 1) + 1 - 1;
                        }
                    }
                }
            }
            return editMapping;
        }

        private void ForestDist(InfoTree ted1, InfoTree ted2, int i, int j, double[][] treedist, double[][] forestdist) {
            forestdist[ted1.GetInfo(POST2_LLD, i - 1) + 1 - 1][ted2.GetInfo(POST2_LLD, j - 1) + 1 - 1] = 0;
            for (int di = ted1.GetInfo(POST2_LLD, i - 1) + 1; di <= i; di++) {
                forestdist[di][ted2.GetInfo(POST2_LLD, j - 1) + 1 - 1] = forestdist[di - 1][ted2.GetInfo(POST2_LLD, j - 1) + 1 - 1] + costDel;
                for (int dj = ted2.GetInfo(POST2_LLD, j - 1) + 1; dj <= j; dj++) {
                    forestdist[ted1.GetInfo(POST2_LLD, i - 1) + 1 - 1][dj] = forestdist[ted1.GetInfo(POST2_LLD, i - 1) + 1 - 1][dj - 1] + costIns;

                    if ((ted1.GetInfo(POST2_LLD, di - 1) == ted1.GetInfo(POST2_LLD, i - 1))
                        && (ted2.GetInfo(POST2_LLD, dj - 1) == ted2.GetInfo(POST2_LLD, j - 1))) {
                        double costRen = 0;
                        if (!(ted1.GetInfo(POST2_LABEL, di - 1) == ted2.GetInfo(POST2_LABEL, dj - 1))) {
                            costRen = costMatch;
                        }
                        forestdist[di][dj] = Math.Min(Math.Min(
                            forestdist[di - 1][dj] + costDel,
                            forestdist[di][dj - 1] + costIns),
                            forestdist[di - 1][dj - 1] + costRen);
                        treedist[di][dj] = forestdist[di][dj];
                    } else {
                        forestdist[di][dj] = Math.Min(Math.Min(
                            forestdist[di - 1][dj] + costDel,
                            forestdist[di][dj - 1] + costIns),
                            forestdist[ted1.GetInfo(POST2_LLD, di - 1) + 1 - 1][ted2.GetInfo(POST2_LLD, dj - 1) + 1 - 1]
                                + treedist[di][dj]);
                    }
                }
            }
        }

    }
}
