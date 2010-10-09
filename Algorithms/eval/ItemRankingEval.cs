// Copyright (C) 2010 Steffen Rendle, Zeno Gantner
//
// This file is part of MyMediaLite.
//
// MyMediaLite is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// MyMediaLite is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with MyMediaLite.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MyMediaLite.data;
using MyMediaLite.data_type;
using MyMediaLite.item_recommender;
using MyMediaLite.rating_predictor;
using MyMediaLite.util;


namespace MyMediaLite.eval
{
	// TODO: think about a better name for the class - we do not compare rankings here ...

    /// <summary>Evaluation class</summary>
    /// <author>Steffen Rendle, Zeno Gantner, University of Hildesheim</author>
    public static class ItemRankingEval
    {
        /// <summary>
        /// Evaluation for rankings of item recommenders. Computes the AUC and precision at different levels.
        /// User-item combinations that appear in both sets are ignored for the test set, and thus in the evaluation.
        ///
        /// Literature:
        ///   C. Manning, P. Raghavan, H. Schütze
        ///   Introduction to Information Retrieval,
        ///   Cambridge University Press, 2008
        /// </summary>
        /// <param name="engine">Item recommender engine</param>
        /// <param name="test">test cases</param>
        /// <param name="train">training data</param>
        /// <param name="relevant_items">a collection of integers with all relevant items</param>
        /// <returns>a dictionary containing the evaluation results</returns>
		static public Dictionary<string, double> EvaluateItemRecommender(
			ItemRecommender engine,
			SparseBooleanMatrix test,
			SparseBooleanMatrix train,
			ICollection<int> relevant_items)
        {
			if (train.Overlap(test) > 0)
				Console.Error.WriteLine("WARNING: Overlapping train and test data");

			// compute evaluation measures
            double auc_sum     = 0;
			double map_sum     = 0;
			double prec_5_sum  = 0;
			double prec_10_sum = 0;
			double prec_15_sum = 0;
			double ndcg_sum    = 0;
            int num_users      = 0;

            foreach (KeyValuePair<int, HashSet<int>> user in test.NonEmptyRows)
            {
                int user_id = user.Key;
                HashSet<int> test_items = user.Value;

				// the number of items that are really relevant for this user
                int num_eval_items = relevant_items.Count - train[user_id].Intersect(relevant_items).Count();

				// skip all users that have 0 or #relevant_items test items
				if (test_items.Count == 0)
					continue;
				if (num_eval_items - test_items.Count == 0)
					continue;

				num_users++;
				int[] prediction = ItemPrediction.PredictItems(engine, user_id, relevant_items);

				auc_sum     += AUC(prediction, test_items, train[user_id]);
				map_sum     += MAP(prediction, test_items, train[user_id]);
				ndcg_sum    += NDCG(prediction, test_items, train[user_id]);
				prec_5_sum  += PrecisionAt(prediction, test_items, train[user_id],  5);
				prec_10_sum += PrecisionAt(prediction, test_items, train[user_id], 10);
				prec_15_sum += PrecisionAt(prediction, test_items, train[user_id], 15);

                if (prediction.Length != relevant_items.Count)
                    throw new Exception("Not all items have been ranked.");
			}

			Dictionary<string, double> result = new Dictionary<string, double>();
			result.Add("AUC",     auc_sum / num_users);
			result.Add("MAP",     map_sum / num_users);
			result.Add("NDCG",    ndcg_sum / num_users);
			result.Add("prec@5",  prec_5_sum / num_users);
			result.Add("prec@10", prec_10_sum / num_users);
			result.Add("prec@15", prec_15_sum / num_users);

			return result;
        }

		/// <summary>
		/// Compute the area under the ROC curve (AUC) of a list of ranked items.
		/// </summary>
		/// <param name="ranked_items">a list of ranked item IDs, the highest-ranking item first</param>
		/// <param name="correct_items">a collection of positive/correct item IDs</param>
		/// <returns>the AUC for the given data</returns>
		public static double AUC(int[] ranked_items, ICollection<int> correct_items)
		{
			return AUC(ranked_items, correct_items, new HashSet<int>());
		}

		/// <summary>
		/// Compute the area under the ROC curve (AUC) of a list of ranked items.
		/// </summary>
		/// <param name="ranked_items">a list of ranked item IDs, the highest-ranking item first</param>
		/// <param name="correct_items">a collection of positive/correct item IDs</param>
		/// <param name="ignore_items">a collection of item IDs which should be ignored for the evaluation</param>
		/// <returns>the AUC for the given data</returns>
		public static double AUC(int[] ranked_items, ICollection<int> correct_items, ICollection<int> ignore_items)
		{
                int num_eval_items = ranked_items.Length - ignore_items.Intersect(ranked_items).Count();
	            int num_eval_pairs = (num_eval_items - correct_items.Count) * correct_items.Count;

				int num_correct_pairs = 0;
				int hit_count         = 0;

	            foreach (int item_id in ranked_items)
				{
					if (ignore_items.Contains(item_id))
						continue;

                    if (!correct_items.Contains(item_id))
                        num_correct_pairs += hit_count;
					else
		            	hit_count++;
                }

                return ((double) num_correct_pairs) / num_eval_pairs;
		}

		// TODO methods w/o ignore

		/// <summary>
		/// Compute the mean average precision (MAP) of a list of ranked items.
		/// </summary>
		/// <param name="ranked_items">a list of ranked item IDs, the highest-ranking item first</param>
		/// <param name="correct_items">a collection of positive/correct item IDs</param>
		/// <returns>the MAP for the given data</returns>
		public static double MAP(int[] ranked_items, ICollection<int> correct_items)
		{
			return MAP(ranked_items, correct_items);
		}

		/// <summary>
		/// Compute the mean average precision (MAP) of a list of ranked items.
		/// </summary>
		/// <param name="ranked_items">a list of ranked item IDs, the highest-ranking item first</param>
		/// <param name="correct_items">a collection of positive/correct item IDs</param>
		/// <param name="ignore_items">a collection of item IDs which should be ignored for the evaluation</param>
		/// <returns>the MAP for the given data</returns>
		public static double MAP(int[] ranked_items, ICollection<int> correct_items, ICollection<int> ignore_items)
		{
            int hit_count       = 0;
			double avg_prec_sum = 0;
			int left_out        = 0;

			for (int i = 0; i < ranked_items.Length; i++)
			{
				int item_id = ranked_items[i];
				if (ignore_items.Contains(item_id))
				{
					left_out++;
					continue;
				}

                if (!correct_items.Contains(item_id))
					continue;

	            hit_count++;

				avg_prec_sum += hit_count / (i + 1 - left_out);
            }

			if (hit_count != 0)
				return avg_prec_sum / hit_count;
			else
				return 0;
		}

		/// <summary>
		/// Compute the normalized discounted cumulative gain (NDCG) of a list of ranked items.
		/// </summary>
		/// <param name="ranked_items">a list of ranked item IDs, the highest-ranking item first</param>
		/// <param name="correct_items">a collection of positive/correct item IDs</param>
		/// <returns>the NDCG for the given data</returns>
		public static double NDCG(int[] ranked_items, ICollection<int> correct_items)
		{
			return NDCG(ranked_items, correct_items);
		}

		/// <summary>
		/// Compute the normalized discounted cumulative gain (NDCG) of a list of ranked items.
		/// </summary>
		/// <param name="ranked_items">a list of ranked item IDs, the highest-ranking item first</param>
		/// <param name="correct_items">a collection of positive/correct item IDs</param>
		/// <param name="ignore_items">a collection of item IDs which should be ignored for the evaluation</param>
		/// <returns>the NDCG for the given data</returns>
		public static double NDCG(int[] ranked_items, ICollection<int> correct_items, ICollection<int> ignore_items)
		{
			double dcg   = 0;
			double idcg  = ComputeIDCG(correct_items.Count);
			int left_out = 0;

            for (int i = 0; i < ranked_items.Length; i++)
			{
				int item_id = ranked_items[i];
				if (ignore_items.Contains(item_id))
				{
					left_out++;
					continue;
				}

                if (!correct_items.Contains(item_id))
					continue;

				// compute NDCG part
				int rank = i + 1 - left_out;
				dcg += 1 / Math.Log(rank + 1, 2);
            }

            return dcg / idcg;
		}

		/// <summary>
		/// Compute the precision@N of a list of ranked items.
		/// </summary>
		/// <param name="ranked_items">a list of ranked item IDs, the highest-ranking item first</param>
		/// <param name="correct_items">a collection of positive/correct item IDs</param>
		/// <param name="n">the cutoff position in the list</param>
		/// <returns>the precision@N for the given data</returns>
		public static double PrecisionAt(int[] ranked_items, ICollection<int> correct_items, int n)
		{
			return PrecisionAt(ranked_items, correct_items, n);
		}

		/// <summary>
		/// Compute the precision@N of a list of ranked items.
		/// </summary>
		/// <param name="ranked_items">a list of ranked item IDs, the highest-ranking item first</param>
		/// <param name="correct_items">a collection of positive/correct item IDs</param>
		/// <param name="ignore_items">a collection of item IDs which should be ignored for the evaluation</param>
		/// <param name="n">the cutoff position in the list</param>
		/// <returns>the precision@N for the given data</returns>
		public static double PrecisionAt(int[] ranked_items, ICollection<int> correct_items, ICollection<int> ignore_items, int n)
		{
			if (n < 1)
				throw new ArgumentException("N must be at least 1.");

            int hit_count = 0;
			int left_out  = 0;

			for (int i = 0; i < ranked_items.Length; i++)
			{
				int item_id = ranked_items[i];
				if (ignore_items.Contains(item_id))
				{
					left_out++;
					continue;
				}

                if (!correct_items.Contains(item_id))
					continue;

				if (i < n + left_out)
					hit_count++;
				else
					break;
            }

			return (double) hit_count / n;
		}

		/// <summary>Computes the ideal DCG given the number of positive items.</summary>
		/// <returns>the ideal DCG</returns>
		/// <param name='n'>the number of positive items</param>
		static double ComputeIDCG(int n)
		{
			double idcg = 0;
			for (int i = 0; i < n; i++)
				idcg += 1 / Math.Log(i + 2, 2);
			return idcg;
		}

		static public ICollection<string> GetRatingPredictionMeasures() {
			string[] measures = { "MAE", "RMSE" };
			return new HashSet<string>(measures);
		}

		static public ICollection<string> GetItemPredictionMeasures() {
			string[] measures = { "AUC", "prec@5", "prec@10", "prec@15", "NDCG", "MAP" };
			return new HashSet<string>(measures);
		}
    }
}
