﻿using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace GrammarParser
{
	public static class ObjectExtentions
	{
		public static T? As<T>(this object? obj) => (T?)obj;
		public static IEnumerable<T?>? AsEnumerable<T>(this object? obj) => obj.As<IEnumerable<T?>?>();
		public static string? AsString(this object? obj) => obj.As<string?>();
		public static char? AsChar(this object? obj) => obj.As<char?>();
		public static int? AsInt(this object? obj) => obj.As<int?>();
		public static long? AsLong(this object? obj) => obj.As<long?>();
		public static float? AsFloat(this object? obj) => obj.As<float?>();
		public static double? AsDouble(this object? obj) => obj.As<double?>();
		public static bool? AsBool(this object? obj) => obj.As<bool?>();
		public static IEnumerable<T?> Wrap<T>(this T? obj) => new[] { obj };
		public static IEnumerable<T?> Wrap<T>(this object? obj) => new[] { obj.As<T?>() };
		public static IEnumerable<T?> JoinEnumerable<T>(this IEnumerable<T?>? obj, IEnumerable<T?>? other) =>
			(obj ?? Enumerable.Empty<T?>()).Concat(other ?? Enumerable.Empty<T?>());
		public static IEnumerable<T?> JoinEnumerable<T>(this object? obj, object? other) =>
			JoinEnumerable(obj.AsEnumerable<T?>(), other.AsEnumerable<T?>());
		public static IEnumerable<T?> JoinAfter<T>(this T? obj, IEnumerable<T?>? other) =>
			(other ?? Enumerable.Empty<T?>()).Append(obj);
		public static IEnumerable<T?> JoinAfter<T>(this object? obj, object? other) =>
			JoinAfter(obj.As<T?>(), other.AsEnumerable<T?>());
		public static IEnumerable<T?> JoinBefore<T>(this T? obj, IEnumerable<T?>? other) =>
			(other ?? Enumerable.Empty<T?>()).Prepend(obj);
		public static IEnumerable<T?> JoinBefore<T>(this object? obj, object? other) =>
			JoinBefore(obj.As<T?>(), other.AsEnumerable<T?>());
	}

	public class GrammarParser
	{
		public static bool LogDebug { get; set; } = false;

		readonly List<TokenType> tokenTypes = new();
		public IGrammarNode? RootGrammarRule { get; set; }
		public bool IgnoreCase { get; set; } = false;

		public TokenNode NewToken(string? name, string regex, Func<Token, object?>? builder = null, bool ignore = false)
		{
			name = $"{name}Token";
			TokenType tokenType = new(name, regex, ignore);
			tokenTypes.Add(tokenType);
			return new TokenNode(name, tokenType, builder);
		}

		bool Tokenize(string str, out TokenStream? tokens)
		{
			tokens = null;
			var tokenList = new List<Token>();
			bool changeMade;
			do
			{
				changeMade = false;
				foreach (var type in tokenTypes)
				{
					changeMade |= type.TryTokenize(ref str, ref tokenList, IgnoreCase);
					if (changeMade) break;
				}
			} while (changeMade);
			if (str.Length != 0) return false;
			tokens = new TokenStream(tokenList);
			return true;
		}

		public IEnumerable<object?> Parse(string str)
		{
			if (RootGrammarRule is null) throw new InvalidOperationException();
			if (!Tokenize(str, out var tokens) || tokens is null)
			{
				Debug.WriteLineIf(LogDebug, "Failed to tokenize the input string");
				yield break;
			}
			Debug.WriteLineIf(LogDebug, "Start to parse the tokens");
			foreach (var result in RootGrammarRule.Parse(tokens))
			{
				if (!tokens.AtEnd)
				{
					Debug.WriteLineIf(LogDebug, $"Parser discards result `{result}` for unparsed tokens: {string.Join(", ", tokens[(tokens.Index + 1)..])}");
					continue;
				}
				Debug.WriteLineIf(LogDebug, $"Parser accepts result `{result}`");
				yield return result;
			}
		}
	}

	public class TokenStream
	{
		readonly Token[] tokens;
		public Token this[int index] => tokens[index];
		public IEnumerable<Token> this[Range range] => tokens[range];
		public int Index { get; set; } = -1;
		public TokenStream(IEnumerable<Token> tokens) => this.tokens = tokens.ToArray();
		public bool AtBegin => Index < 0 &&
			(Index == -1 ? true : throw new IndexOutOfRangeException());
		public bool AtEnd => Index >= tokens.Length - 1 &&
			(Index == tokens.Length - 1 ? true : throw new IndexOutOfRangeException());
		public Token? Current => AtBegin ? null : tokens[Index];
		public Token? Next() => AtEnd ? null : tokens[++Index];
	}

	public class InstanceName
	{
		readonly Type? type;
		public string? Value { get; set; }
		public override string? ToString() => Value ?? type?.Name;
		public InstanceName(Type type, string? name = null)
		{
			this.type = type;
			Value = name;
		}
	}

	public interface INameable
	{
		public InstanceName Name { get; }
	}

	public class TokenType : INameable
	{
		public InstanceName Name { get; }
		public override string? ToString() => $"{Name}(\"{RegexPattern}\")";
		public string RegexPattern { get; }
		public bool Ignore { get; }
		public TokenType(string? name, string regex, bool ignore = false)
		{
			Name = new(GetType(), name);
			RegexPattern = regex;
			Ignore = ignore;
		}
		public bool TryTokenize(ref string str, ref List<Token> tokens, bool ignoreCase)
		{
			var match = Regex.Match(str, $"^{RegexPattern}", ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
			if (match.Success)
			{
				Token token = new(this, match.Value);
				str = str[match.Length..];
				if (!Ignore)
				{
					Debug.WriteLineIf(GrammarParser.LogDebug, $"Got token: {token}");
					tokens.Add(token);
				}
			}
			return match.Success;
		}
	}

	public class Token
	{
		public override string? ToString() => $"{Type.Name}(\"{Value}\")";
		public TokenType Type { get; set; }
		public string Value { get; set; }
		public Token(TokenType type, string value)
		{
			Type = type;
			Value = value;
		}
	}

	public interface IGrammarNode : INameable
	{
		public IEnumerable<object?> Parse(TokenStream tokens);
	}

	public class EmptyNode : IGrammarNode
	{
		public InstanceName Name { get; }
		public override string? ToString() => $"{Name}";
		public Func<object?>? Builder { get; set; }
		public EmptyNode(string? name = null, Func<object?>? builder = null)
		{
			Name = new(GetType(), name);
			Builder = builder;
		}
		public IEnumerable<object?> Parse(TokenStream tokens)
		{
			Debug.WriteLineIf(GrammarParser.LogDebug, $"@\"{tokens.Current?.Value}\"\t`{this}.Parse` yields `{null}`");
			yield return null;
		}
	}

	public class TokenNode : IGrammarNode
	{
		public InstanceName Name { get; }
		public override string? ToString() => $"{Name}";
		public TokenType? TokenType { get; set; }
		public Func<Token, object?>? Builder { get; set; }
		public TokenNode(string? name = null, TokenType? tokenType = null, Func<Token, object?>? builder = null)
		{
			Name = new(GetType(), name);
			TokenType = tokenType;
			Builder = builder;
		}
		public TokenNode(string? name, TokenNode tokenNode) : this(name, tokenNode.TokenType, tokenNode.Builder) { }
		public IEnumerable<object?> Parse(TokenStream tokens)
		{
			var token = tokens.Next();
			if (token is null || token.Type != TokenType)
			{
				Debug.WriteLineIf(GrammarParser.LogDebug, $"@\"{tokens.Current?.Value}\"\t`{this}.Parse` yields nothing");
				yield break;
			}
			var result = Builder?.Invoke(token);
			Debug.WriteLineIf(GrammarParser.LogDebug, $"@\"{tokens.Current?.Value}\"\t`{this}.Parse` yields `{result}`");
			yield return result;
		}
	}

	public class SequenceNode : IGrammarNode
	{
		public InstanceName Name { get; }
		public override string? ToString() => $"{Name}({string.Join(", ", Children.Select(ch => ch.Name))})";
		public List<IGrammarNode> Children { get; set; } = new();
		public Func<object?[], object?>? Builder { get; set; }
		public SequenceNode(string? name = null) => Name = new(GetType(), name);
		public TChild NewChild<TChild>(TChild child) where TChild : IGrammarNode
		{
			Children.Add(child);
			return child;
		}
		public void SetChildren(params IGrammarNode[] children) => Children = children.ToList();
		IEnumerable<IEnumerable<object?>> ParseRecursive(TokenStream tokens, int childIndex)
		{
			if (childIndex >= Children.Count) yield break;
			var ch = Children[childIndex];
			Debug.WriteLineIf(GrammarParser.LogDebug, $"@\"{tokens.Current?.Value}\"\t`{this}.ParseRecursive` calls `{ch}.Parse` at child {childIndex} `{Children[childIndex]}`");
			foreach (var chResult in ch.Parse(tokens))
			{
				int checkpoint = tokens.Index;
				var chResultAsArray = new[] { chResult };
				if (childIndex == Children.Count - 1)
				{
					Debug.WriteLineIf(GrammarParser.LogDebug, $"@\"{tokens.Current?.Value}\"\t`{this}.ParseRecursive` yields `{chResult}` at child {childIndex} `{Children[childIndex]}`");
					yield return chResultAsArray;
				}
				else
				{
					tokens.Index = checkpoint;
					foreach (var restResults in ParseRecursive(tokens, childIndex + 1))
					{
						Debug.WriteLineIf(GrammarParser.LogDebug, $"@\"{tokens.Current?.Value}\"\t`{this}.ParseRecursive` yields `{chResult}` at child {childIndex} `{Children[childIndex]}`");
						yield return chResultAsArray.Concat(restResults);
					}
				}
			}
		}
		public IEnumerable<object?> Parse(TokenStream tokens)
		{
			foreach (var childrenResults in ParseRecursive(tokens, 0))
			{
				Debug.WriteLineIf(GrammarParser.LogDebug, $"@\"{tokens.Current?.Value}\"\t`{this}.Parse` yields `{childrenResults}`");
				yield return Builder?.Invoke(childrenResults.ToArray());
			}
		}
	}

	public class MultipleNode : IGrammarNode
	{
		public InstanceName Name { get; }
		public override string? ToString() => $"{Name}[{string.Join(" | ", Children.Select(ch => ch.Name))}]";
		public List<IGrammarNode> Children { get; set; } = new();
		public MultipleNode(string? name = null) => Name = new(GetType(), name);
		string? RenameChild(IGrammarNode child, int index) =>
			child is SequenceNode ? child.Name.Value ??= $"{Name}_{index}" : child.Name.ToString();
		public TChild NewChild<TChild>(TChild child) where TChild : IGrammarNode
		{
			RenameChild(child, Children.Count);
			Children.Add(child);
			return child;
		}
		public void SetChildren(params IGrammarNode[] children)
		{
			Children = children.ToList();
			_ = Children.Select(RenameChild);
		}
		public IEnumerable<object?> Parse(TokenStream tokens)
		{
			var checkpoint = tokens.Index;
			foreach (var ch in Children)
			{
				tokens.Index = checkpoint;
				Debug.WriteLineIf(GrammarParser.LogDebug, $"@\"{tokens.Current?.Value}\"\t`{this}.Parse` calls `{ch}.Parse`");
				foreach (var result in ch.Parse(tokens))
				{
					Debug.WriteLineIf(GrammarParser.LogDebug, $"@\"{tokens.Current?.Value}\"\t`{this}.Parse` yields `{result}`");
					yield return result;
				}
			}
		}
	}
}
