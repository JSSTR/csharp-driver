﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Collections;
using System.Text.RegularExpressions;

namespace Cassandra.Data
{
    internal class CqlQueryEvaluator
    {
        ICqlTable table;

        internal CqlQueryEvaluator(ICqlTable table)
		{
            this.table = table;

            SelectFieldsArray = new List<string>();
			OrderByFieldsArray = new List<string>();
		}

        public string Query
		{
			get
			{
				var select = Fields;
                var from = table.GetTableName().CqlIdentifier();
				var where = WhereCriteria;
				var orderBy = OrderBy;
				var limit = LimitCount;

				var query = string.Format("SELECT {0} \nFROM {1}", select, from);

				if (!string.IsNullOrWhiteSpace(where))
					query += " \nWHERE " + where;

				if (!string.IsNullOrEmpty(orderBy))
					query += " \nORDER BY " + orderBy;

				if (limit > 0)
					query += " \nLIMIT " + limit;

				return query;
			}
		}

        public string CountQuery
        {
            get
            {
                var select = Fields;
                var from = table.GetTableName().CqlIdentifier();
                var where = WhereCriteria;
                var orderBy = OrderBy;
                var limit = LimitCount;

                var query = string.Format("SELECT count(*) \nFROM {1}", select, from);

                if (!string.IsNullOrWhiteSpace(where))
                    query += " \nWHERE " + where;

                if (!string.IsNullOrEmpty(orderBy))
                    query += " \nORDER BY " + orderBy;

                if (limit > 0)
                    query += " \nLIMIT " + limit;

                return query;
            }
        }
        
        private IList<string> SelectFieldsArray { get; set; }

		private IList<string> OrderByFieldsArray { get; set; }

		private int FirstCount { get; set; }

		private int LimitCount { get; set; }

		private string OrderBy
		{
			get
			{
				return string.Join(", ", OrderByFieldsArray);
			}
		}

		private string Fields
		{
			get
			{
				var firstCount = FirstCount;
				var query = "";

				if (firstCount > 0)
					query += "FIRST " + firstCount;

				if (query.Length > 0)
					query += " ";

				if (SelectFieldsArray.Count == 0)
				{
					query += "*";
					return query;
				}

				return string.Join(", ", SelectFieldsArray);
			}
		}

		private string WhereCriteria { get; set; }

        //private void AddTable(ICassandraColumnFamilyInfo provider)
        //{
        //    _columnFamily = provider.FamilyName;
        //    _schema = provider.GetSchema();
        //}

		private void AddOrderByFieldDescending(Expression exp)
		{
			OrderByFieldsArray.Add(GetPropertyName(exp) + " DESC");
		}

		private void AddOrderByFieldAscending(Expression exp)
		{
			OrderByFieldsArray.Add(GetPropertyName(exp) + " ASC");
		}

		private void AddSelectField(Expression exp)
		{
			foreach (var f in VisitSelectExpression(exp))
                SelectFieldsArray.Add(f.ToString().CqlIdentifier());
		}

		private void AddCriteria(Expression exp)
		{
			string newCriteria = VisitWhereExpression(exp);

			if (!string.IsNullOrEmpty(WhereCriteria))
				WhereCriteria = "(" + WhereCriteria + " AND " + newCriteria + ")";
			else
				WhereCriteria = newCriteria;
		}

		#region Expression Helpers

		private Expression SimplifyExpression(Expression exp)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Convert:
				case ExpressionType.Quote:
					return SimplifyExpression(((UnaryExpression)exp).Operand);

				case ExpressionType.Lambda:
					return SimplifyExpression(((LambdaExpression)exp).Body);

				default:
					return exp;
			}
		}

		private Type GetBaseType(MemberExpression exp)
		{
			while (exp.Expression.NodeType == ExpressionType.MemberAccess)
				exp = (MemberExpression)exp.Expression;

			return exp.Expression.Type;
		}

		private string GetPropertyName(Expression exp)
		{
            if (exp.Type.GetInterface("ICqlToken") != null)
            {
                return "token("+GetPropertyName((exp as MethodCallExpression).Arguments[0])+")";
            }

			exp = SimplifyExpression(exp);

			switch (exp.NodeType)
			{
				case ExpressionType.MemberAccess:
					var memExp = (MemberExpression)exp;
					var name = memExp.Member.Name;

					// if object queries
//					if (GetBaseType(memExp) != typeof(ICqlRow))
	//					return name;

//					if (name != "Key")
	//					throw new NotSupportedException(name + " is not a supported property.");

		//			return _schema.KeyName.GetValue<string>();
                    return name.CqlIdentifier();

				case ExpressionType.Call:

					var field = SimplifyExpression(((MethodCallExpression)exp).Arguments[0]);

					if (field.NodeType != ExpressionType.Constant)
						throw new NotSupportedException(exp.NodeType.ToString() + " is not supported.");

                    return ((ConstantExpression)field).Value.ToString().CqlIdentifier();

				default:
					throw new NotSupportedException(exp.NodeType.ToString() + " is not supported.");
			}
		}

		#endregion

		#region Expression Parsing



		public void Evaluate(Expression exp, Action<string> call = null)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Lambda:
					Evaluate(((LambdaExpression)exp).Body);
					break;

				case ExpressionType.Call:
					VisitMethodCall((MethodCallExpression)exp);
					break;

				case ExpressionType.MemberInit:
					VisitMemberInit((MemberInitExpression)exp, call);
					break;

				case ExpressionType.Constant:
					var obj = ((ConstantExpression)exp).Value;
//					var family = obj as ICassandraColumnFamilyInfo;

//					AddTable(family);
					break;
			}
		}

		private void VisitMemberInit(MemberInitExpression exp, Action<string> call)
		{
			foreach (MemberAssignment member in exp.Bindings)
				call(GetPropertyName(member.Expression));
		}

		private void VisitMethodCall(MethodCallExpression exp)
		{
			Evaluate(exp.Arguments[0]);

			if (exp.Method.Name == "Where")
				AddCriteria(exp.Arguments[1]);
			else if (exp.Method.Name == "Select")
				AddSelectField(SimplifyExpression(exp.Arguments[1]));
			else if (exp.Method.Name == "Take")
				SetLimit(exp.Arguments[1]);
			else if (exp.Method.Name == "OrderBy" || exp.Method.Name == "ThenBy")
				AddOrderByFieldAscending(SimplifyExpression(exp.Arguments[1]));
			else if (exp.Method.Name == "OrderByDescending" || exp.Method.Name == "ThenByDescending")
				AddOrderByFieldDescending(SimplifyExpression(exp.Arguments[1]));
			else
				throw new NotSupportedException("Method call to " + exp.Method.Name + " is not supported.");
		}

		private void SetLimit(Expression exp)
		{
			if (exp.NodeType == ExpressionType.Constant)
				LimitCount = (int)((ConstantExpression)exp).Value;
		}

		private IEnumerable<object> VisitSelectExpression(Expression exp)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Parameter:
					return new object[0];

				case ExpressionType.Constant:
					return VisitSelectColumnExpression((ConstantExpression)exp);

				case ExpressionType.MemberAccess:
					var list = new List<object>();
					VisitSelectMemberAccess((MemberExpression)exp, x => {
						list.Add(x);
					});
					return list;

				case ExpressionType.New:
					return VisitSelectNew((NewExpression)exp);

                case ExpressionType.MemberInit:
                    return VisitSelectMemberInit((MemberInitExpression)exp);
                
                default:
					throw new NotSupportedException(exp.NodeType.ToString() + " is not supported.");
			}
		}

		private IEnumerable<object> VisitSelectNew(NewExpression exp)
		{
			var list = new List<object>();

			foreach (var arg in exp.Arguments)
				VisitSelectMemberAccess((MemberExpression)arg, x => {
					list.Add(x);
				});

			return list;
		}

        public Dictionary<string, string> AlternativeMapping = new Dictionary<string, string>();

        private IEnumerable<object> VisitSelectMemberInit(MemberInitExpression exp)
        {
            var list = new List<object>();

            foreach (var binding in exp.Bindings)
            {
                var e = (binding as MemberAssignment).Expression as MemberExpression;
                var name = e.Member.Name;
                list.Add(name);
                AlternativeMapping.Add( binding.Member.Name,name);
            }
            return list;
        }
        
        private void VisitSelectMemberAccess(MemberExpression exp, Action<string> call)
		{
			var name = exp.Member.Name;
			call(name);
		}

		private IEnumerable<object> VisitSelectColumnExpression(ConstantExpression exp)
		{
			return (IEnumerable<object>)exp.Value;
		}

		private string VisitWhereExpression(Expression exp)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Convert:
				case ExpressionType.Lambda:
				case ExpressionType.Quote:
					return VisitWhereExpression(SimplifyExpression(exp));

				case ExpressionType.Not:
					return VisitWhereUnaryExpression((UnaryExpression)exp);

				case ExpressionType.Equal:
				case ExpressionType.NotEqual:
				case ExpressionType.GreaterThan:
				case ExpressionType.GreaterThanOrEqual:
				case ExpressionType.LessThan:
				case ExpressionType.LessThanOrEqual:
					return VisitWhereRelationalExpression((BinaryExpression)exp);

				case ExpressionType.And:
				case ExpressionType.AndAlso:
				case ExpressionType.Or:
				case ExpressionType.OrElse:
					return VisitWhereConditionalExpression((BinaryExpression)exp);

				case ExpressionType.Call:
					return VisitWhereMethodCallExpression((MethodCallExpression)exp);

				default:
					throw new NotSupportedException(exp.NodeType.ToString() + " is not supported.");
			}
		}

		private string VisitWhereUnaryExpression(UnaryExpression exp)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Not:
					return "NOT (" + VisitWhereExpression(exp.Operand) + ")";

				default:
					throw new NotSupportedException(exp.NodeType.ToString() + " is not a supported unary criteria.");
			}
		}

        private string RightObjectToString(object obj)
        {
            if (obj is ICqlToken)
                return string.Concat("token (", (obj as ICqlToken).Value.Encode(), ")");
            else
                return obj.Encode();
        }

		private string VisitWhereMethodCallExpression(MethodCallExpression exp)
		{
            if (exp.Method.Name == "Contains")
            {
                if (exp.Arguments.Count > 1)
                {
                    var left = GetPropertyName(exp.Arguments[1]);
                    var values = (IEnumerable)Expression.Lambda(exp.Arguments[0]).Compile().DynamicInvoke();
                    var rightArray = new List<string>();
                    foreach (var obj in values)
                        rightArray.Add(RightObjectToString(obj));
                    var right = string.Join(",", rightArray);
                    return left + " IN (" + right + ")";
                }
                else
                {
                    var left = GetPropertyName(exp.Arguments[0]);
                    var values = (IEnumerable)Expression.Lambda(exp.Object).Compile().DynamicInvoke();
                    var rightArray = new List<string>();
                    foreach (var obj in values)
                        rightArray.Add(RightObjectToString(obj));
                    var right = string.Join(",", rightArray);
                    return left + " IN (" + right + ")";
                }
            }
            else
                throw new NotSupportedException("Method call to " + exp.Method.Name + " is not supported.");
		}

		private string VisitWhereRelationalExpression(BinaryExpression exp)
		{
			string criteria;

			string left = GetPropertyName(exp.Left);
			object rightObj = Expression.Lambda(exp.Right).Compile().DynamicInvoke();
			string right = RightObjectToString(rightObj);

			//if (left == "KEY")
			{
				switch (exp.NodeType)
				{
					case ExpressionType.Equal:
						criteria = left + " = " + right;
						break;
					case ExpressionType.GreaterThan:
						criteria = left + " > " + right;
						break;
					case ExpressionType.GreaterThanOrEqual:
						criteria = left + " >= " + right;
						break;
					case ExpressionType.LessThan:
						criteria = left + " < " + right;
						break;
					case ExpressionType.LessThanOrEqual:
						criteria = left + " <= " + right;
						break;

					default:
						throw new NotSupportedException(
							exp.NodeType.ToString() + " is not a supported relational criteria for KEY.");
				}
			}
            //else
            //{
            //    switch (exp.NodeType)
            //    {
            //        case ExpressionType.Equal:
            //            criteria = left + " = " + right;
            //            break;

            //        default:
            //            throw new NotSupportedException(
            //                exp.NodeType.ToString() + " is not a supported relational criteria for Columns.");
            //    }
            //}

			return criteria;
		}

		private string VisitWhereConditionalExpression(BinaryExpression exp)
		{
			string criteria;

			switch (exp.NodeType)
			{
				case ExpressionType.And:
				case ExpressionType.AndAlso:
					criteria = VisitWhereExpression(exp.Left) + " AND " + VisitWhereExpression(exp.Right);
					break;

				default:
					throw new NotSupportedException(exp.NodeType.ToString() + " is not a supported conditional criteria.");
			}

			return criteria;
		}
		
    }
        #endregion
}