﻿using SyntaxParser.Shared;

namespace SyntaxParser.Demo.Parsers.Sql
{
	public class SelectStmt
	{
		public IEnumerable<Expr?>? Columns { get; set; }
		public IEnumerable<Relation?>? Tables { get; set; }
		public Expr? Condition { get; set; }

		public enum ToStringType { Full, Short, Sql }
		public string? ToString(ToStringType type) => type switch
		{
			ToStringType.Full => $"{GetType().Name} {{\n\tcolumns: [\n\t\t{string.Join(",\n\t\t", Columns ?? Enumerable.Empty<Expr?>())}\n\t],\n\ttables: [\n\t\t{string.Join(",\n\t\t", Tables ?? Enumerable.Empty<Relation?>())}\n\t],\n\tcondition: {Condition?.ToString() ?? "null"}\n}}",
			ToStringType.Short => $"{{ columns: [ {string.Join(", ", Columns ?? Enumerable.Empty<Expr?>())} ], tables: [ {string.Join(", ", Tables ?? Enumerable.Empty<Relation?>())} ], condition: {Condition?.ToString() ?? "null"} }}",
			ToStringType.Sql => $"SELECT {string.Join(", ", Columns ?? Enumerable.Empty<Expr?>())}" +
				($" FROM {string.Join(", ", Tables ?? Enumerable.Empty<Relation?>())}").If(Tables?.Any()) +
				($" WHERE {Condition}").If(Condition is not null),
			_ => throw new NotSupportedException(),
		};
		public override string? ToString() => ToString(ToStringType.Full);
	}

	public class Relation
	{
		public string? Name { get; set; }
		public string? Alias { get; set; }

		public override string? ToString() => $"{Name}{(Alias is null ? null : $" (alias: {Alias})")}";
	}

	public class Attr
	{
		public string? RelationName { get; set; }
		public string? FieldName { get; set; }

		public override string? ToString() =>
			$"{(RelationName is null ? null : $"{RelationName}.")}{FieldName}";
	}

	public class Value
	{
		public object? ValueObj { get; set; }
		public Value(object? value) => ValueObj = value;

		public Type? Type => ValueObj?.GetType();
		public bool IsNumeric => Type.IsIn(null, typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal));
		public double? GetNumeric() => IsNumeric ? Convert.ToDouble(ValueObj) : null;

		public override string? ToString() => $"{ValueObj}";
	}

	public static class Operator
	{
		public enum Comp { Undefined = 0 << 4, Eq, Ne, Lt, Le, Gt, Ge, In, NotIn, Exists }
		public enum Arith { Undefined = 1 << 4, Add, Subtract, Multiply, Divide, Negative }
		public enum Logical { Undefined = 2 << 4, And, Or, Not }

		public static string? ToString<TOperator>(TOperator? oper) where TOperator : struct, Enum =>
			oper switch
			{
				Comp.Eq => " = ",
				Comp.Ne => " <> ",
				Comp.Lt => " < ",
				Comp.Le => " <= ",
				Comp.Gt => " > ",
				Comp.Ge => " >= ",
				Comp.In => " IN ",
				Comp.NotIn => " NOT IN ",
				Comp.Exists => "EXISTS ",
				Arith.Add => " + ",
				Arith.Subtract => " - ",
				Arith.Multiply => " * ",
				Arith.Divide => " / ",
				Arith.Negative => "-",
				Logical.And => " AND ",
				Logical.Or => " OR ",
				Logical.Not => "NOT ",
				null => null,
				_ => throw new NotSupportedException(),
			};
	}

	public abstract class Expr
	{
		public string? Alias { get; set; }

		public enum ToStringOptions
		{
			None = 0,
			WithBrackets = 1 << 0,
		}
		public string? ToString(ToStringOptions options)
		{
			string? result = ToString();
			if ((options & ToStringOptions.WithBrackets) != 0)
			{
				if (this is not (ValueExpr or AttrExpr)
					|| (this is ValueExpr valueExpr && valueExpr.Value?.GetNumeric() is < 0))
				{
					result = $"({result})";
				}
			}
			return result;
		}
		public string? ToString(string? str) => $"{str}{(Alias is null ? null : $" (alias: {Alias})")}";
		public override string? ToString() => $"{Alias}";
	}

	public class ValueExpr : Expr
	{
		public Value? Value { get; set; }
		public ValueExpr(Value? value) => Value = value;

		public override string? ToString() => ToString($"{Value}");
	}

	public class AttrExpr : Expr
	{
		public Attr? Attr { get; set; }
		public AttrExpr(Attr? attr) => Attr = attr;

		public override string? ToString() => ToString($"{Attr}");
	}

	public class ParensExpr : Expr
	{
		public Expr? Child { get; set; }
		public ParensExpr(Expr? child) => Child = child;

		public override string? ToString() => ToString($"{Child}");
	}

	public class SubqueryExpr : Expr
	{
		public SelectStmt? Subquery { get; set; }
		public SubqueryExpr(SelectStmt? subquery) => Subquery = subquery;

		public override string? ToString() => ToString($"{GetType().Name} {Subquery?.ToString(SelectStmt.ToStringType.Short)}");
	}

	public class OperatorExpr<TOperator> : Expr where TOperator : struct, Enum
	{
		public class Unit
		{
			public TOperator? Oper { get; set; }
			public Expr? Expr { get; set; }
			public Unit(TOperator? oper, Expr? expr)
			{
				Oper = oper;
				Expr = expr;
			}
		}

		public IEnumerable<Unit?>? Children { get; set; }
		public OperatorExpr(IEnumerable<Unit?>? children) => Children = children;
		public static OperatorExpr<TOperator> Binary(Expr? left, TOperator? oper, Expr? right)
			=> new(new Unit[] { new(null, left), new(oper, right) });
		public static OperatorExpr<TOperator> Unary(TOperator? oper, Expr? right)
			=> new(new Unit(oper, right).Array());
		public static OperatorExpr<TOperator> JoinRest(Expr? expr, TOperator? oper, OperatorExpr<TOperator>? other)
		{
			var unit = new Unit(null, expr);
			var nextUnit = other?.Children?.Any() is true
				? new Unit(oper, other?.Children?.FirstOrDefault()?.Expr)
				: null;
			var restChildren = other?.Children?.Skip(1);
			return new(new[] { unit, nextUnit }.ConcatBefore(restChildren));
		}

		public bool IsUnary => Children?.FirstOrDefault()?.Oper is not null;

		public override string? ToString()
		{
			var childStrs = Children?
				.Select(ch => $"{Operator.ToString(ch?.Oper)}{ch?.Expr?.ToString(ToStringOptions.WithBrackets)}")
				?? Enumerable.Empty<string?>();
			return ToString(string.Concat(childStrs));
		}
	}
}
