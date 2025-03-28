﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NStack;

namespace Terminal.Gui {
	/// <summary>
	/// Text alignment enumeration, controls how text is displayed.
	/// </summary>
	public enum TextAlignment {
		/// <summary>
		/// Aligns the text to the left of the frame.
		/// </summary>
		Left,
		/// <summary>
		/// Aligns the text to the right side of the frame.
		/// </summary>
		Right,
		/// <summary>
		/// Centers the text in the frame.
		/// </summary>
		Centered,
		/// <summary>
		/// Shows the text as justified text in the frame.
		/// </summary>
		Justified
	}

	/// <summary>
	/// Provides text formatting capabilities for console apps. Supports, hotkeys, horizontal alignment, multiple lines, and word-based line wrap.
	/// </summary>
	public class TextFormatter {
		List<ustring> lines = new List<ustring> ();
		ustring text;
		TextAlignment textAlignment;
		Attribute textColor = -1;
		bool needsFormat;
		Key hotKey;
		Size size;

		/// <summary>
		///   The text to be displayed. This text is never modified.
		/// </summary>
		public virtual ustring Text {
			get => text;
			set {
				text = value;

				if (text.RuneCount > 0 && (Size.Width == 0 || Size.Height == 0 || Size.Width != text.RuneCount)) {
					// Provide a default size (width = length of longest line, height = 1)
					// TODO: It might makes more sense for the default to be width = length of first line?
					Size = new Size (TextFormatter.MaxWidth (Text, int.MaxValue), 1);
				}

				NeedsFormat = true;
			}
		}

		// TODO: Add Vertical Text Alignment
		/// <summary>
		/// Controls the horizontal text-alignment property. 
		/// </summary>
		/// <value>The text alignment.</value>
		public TextAlignment Alignment {
			get => textAlignment;
			set {
				textAlignment = value;
				NeedsFormat = true;
			}
		}

		/// <summary>
		///  Gets or sets the size of the area the text will be constrained to when formatted.
		/// </summary>
		public Size Size {
			get => size;
			set {
				size = value;
				NeedsFormat = true;
			}
		}

		/// <summary>
		/// The specifier character for the hotkey (e.g. '_'). Set to '\xffff' to disable hotkey support for this View instance. The default is '\xffff'.
		/// </summary>
		public Rune HotKeySpecifier { get; set; } = (Rune)0xFFFF;

		/// <summary>
		/// The position in the text of the hotkey. The hotkey will be rendered using the hot color.
		/// </summary>
		public int HotKeyPos { get => hotKeyPos; set => hotKeyPos = value; }

		/// <summary>
		/// Gets the hotkey. Will be an upper case letter or digit.
		/// </summary>
		public Key HotKey { get => hotKey; internal set => hotKey = value; }

		/// <summary>
		/// Specifies the mask to apply to the hotkey to tag it as the hotkey. The default value of <c>0x100000</c> causes
		/// the underlying Rune to be identified as a "private use" Unicode character.
		/// </summary>HotKeyTagMask
		public uint HotKeyTagMask { get; set; } = 0x100000;

		/// <summary>
		/// Gets the cursor position from <see cref="HotKey"/>. If the <see cref="HotKey"/> is defined, the cursor will be positioned over it.
		/// </summary>
		public int CursorPosition { get; set; }

		/// <summary>
		/// Gets the formatted lines.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Upon a 'get' of this property, if the text needs to be formatted (if <see cref="NeedsFormat"/> is <c>true</c>)
		/// <see cref="Format(ustring, int, TextAlignment, bool, bool)"/> will be called internally. 
		/// </para>
		/// </remarks>
		public List<ustring> Lines {
			get {
				// With this check, we protect against subclasses with overrides of Text
				if (ustring.IsNullOrEmpty (Text)) {
					lines = new List<ustring> ();
					lines.Add (ustring.Empty);
					NeedsFormat = false;
					return lines;
				}

				if (NeedsFormat) {
					var shown_text = text;
					if (FindHotKey (text, HotKeySpecifier, true, out hotKeyPos, out hotKey)) {
						shown_text = RemoveHotKeySpecifier (Text, hotKeyPos, HotKeySpecifier);
						shown_text = ReplaceHotKeyWithTag (shown_text, hotKeyPos);
					}
					if (Size.IsEmpty) {
						throw new InvalidOperationException ("Size must be set before accessing Lines");
					}
					lines = Format (shown_text, Size.Width, textAlignment, Size.Height > 1);
					NeedsFormat = false;
				}
				return lines;
			}
		}

		/// <summary>
		/// Gets or sets whether the <see cref="TextFormatter"/> needs to format the text when <see cref="Draw(Rect, Attribute, Attribute)"/> is called.
		/// If it is <c>false</c> when Draw is called, the Draw call will be faster.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is set to true when the properties of <see cref="TextFormatter"/> are set.
		/// </para>
		/// </remarks>
		public bool NeedsFormat { get => needsFormat; set => needsFormat = value; }

		static ustring StripCRLF (ustring str)
		{
			var runes = str.ToRuneList ();
			for (int i = 0; i < runes.Count; i++) {
				switch (runes [i]) {
				case '\n':
					runes.RemoveAt (i);
					break;

				case '\r':
					if ((i + 1) < runes.Count && runes [i + 1] == '\n') {
						runes.RemoveAt (i);
						runes.RemoveAt (i + 1);
						i++;
					} else {
						runes.RemoveAt (i);
					}
					break;
				}
			}
			return ustring.Make (runes);
		}
		static ustring ReplaceCRLFWithSpace (ustring str)
		{
			var runes = str.ToRuneList ();
			for (int i = 0; i < runes.Count; i++) {
				switch (runes [i]) {
				case '\n':
					runes [i] = (Rune)' ';
					break;

				case '\r':
					if ((i + 1) < runes.Count && runes [i + 1] == '\n') {
						runes [i] = (Rune)' ';
						runes.RemoveAt (i + 1);
						i++;
					} else {
						runes [i] = (Rune)' ';
					}
					break;
				}
			}
			return ustring.Make (runes);
		}

		/// <summary>
		/// Formats the provided text to fit within the width provided using word wrapping.
		/// </summary>
		/// <param name="text">The text to word wrap</param>
		/// <param name="width">The width to contain the text to</param>
		/// <param name="preserveTrailingSpaces">If <c>true</c>, the wrapped text will keep the trailing spaces.
		///  If <c>false</c>, the trailing spaces will be trimmed.</param>
		/// <returns>Returns a list of word wrapped lines.</returns>
		/// <remarks>
		/// <para>
		/// This method does not do any justification.
		/// </para>
		/// <para>
		/// This method strips Newline ('\n' and '\r\n') sequences before processing.
		/// </para>
		/// </remarks>
		public static List<ustring> WordWrap (ustring text, int width, bool preserveTrailingSpaces = false)
		{
			if (width < 0) {
				throw new ArgumentOutOfRangeException ("Width cannot be negative.");
			}

			int start = 0, end;
			var lines = new List<ustring> ();

			if (ustring.IsNullOrEmpty (text)) {
				return lines;
			}

			var runes = StripCRLF (text).ToRuneList ();

			while ((end = start + width) < runes.Count) {
				while (runes [end] != ' ' && end > start)
					end -= 1;
				if (end == start)
					end = start + width;
				lines.Add (ustring.Make (runes.GetRange (start, end - start)));
				start = end;
				if (runes [end] == ' ' && !preserveTrailingSpaces) {
					start++;
				}
			}

			if (start < text.RuneCount) {
				lines.Add (ustring.Make (runes.GetRange (start, runes.Count - start)));
			}

			return lines;
		}

		/// <summary>
		/// Justifies text within a specified width. 
		/// </summary>
		/// <param name="text">The text to justify.</param>
		/// <param name="width">If the text length is greater that <c>width</c> it will be clipped.</param>
		/// <param name="talign">Alignment.</param>
		/// <returns>Justified and clipped text.</returns>
		public static ustring ClipAndJustify (ustring text, int width, TextAlignment talign)
		{
			if (width < 0) {
				throw new ArgumentOutOfRangeException ("Width cannot be negative.");
			}
			if (ustring.IsNullOrEmpty (text)) {
				return text;
			}

			var runes = text.ToRuneList ();
			int slen = runes.Count;
			if (slen > width) {
				return ustring.Make (runes.GetRange (0, width));
			} else {
				if (talign == TextAlignment.Justified) {
					return Justify (text, width);
				}
				return text;
			}
		}

		/// <summary>
		/// Justifies the text to fill the width provided. Space will be added between words (demarked by spaces and tabs) to
		/// make the text just fit <c>width</c>. Spaces will not be added to the ends.
		/// </summary>
		/// <param name="text"></param>
		/// <param name="width"></param>
		/// <param name="spaceChar">Character to replace whitespace and pad with. For debugging purposes.</param>
		/// <returns>The justified text.</returns>
		public static ustring Justify (ustring text, int width, char spaceChar = ' ')
		{
			if (width < 0) {
				throw new ArgumentOutOfRangeException ("Width cannot be negative.");
			}
			if (ustring.IsNullOrEmpty (text)) {
				return text;
			}

			var words = text.Split (ustring.Make (' '));
			int textCount = words.Sum (arg => arg.RuneCount);

			var spaces = words.Length > 1 ? (width - textCount) / (words.Length - 1) : 0;
			var extras = words.Length > 1 ? (width - textCount) % words.Length : 0;

			var s = new System.Text.StringBuilder ();
			for (int w = 0; w < words.Length; w++) {
				var x = words [w];
				s.Append (x);
				if (w + 1 < words.Length)
					for (int i = 0; i < spaces; i++)
						s.Append (spaceChar);
				if (extras > 0) {
					extras--;
				}
			}
			return ustring.Make (s.ToString ());
		}

		static char [] whitespace = new char [] { ' ', '\t' };
		private int hotKeyPos;

		/// <summary>
		/// Reformats text into lines, applying text alignment and optionally wrapping text to new lines on word boundaries.
		/// </summary>
		/// <param name="text"></param>
		/// <param name="width">The width to bound the text to for word wrapping and clipping.</param>
		/// <param name="talign">Specifies how the text will be aligned horizontally.</param>
		/// <param name="wordWrap">If <c>true</c>, the text will be wrapped to new lines as need. If <c>false</c>, forces text to fit a single line. Line breaks are converted to spaces. The text will be clipped to <c>width</c></param>
		/// <param name="preserveTrailingSpaces">If <c>true</c> and 'wordWrap' also true, the wrapped text will keep the trailing spaces. If <c>false</c>, the trailing spaces will be trimmed.</param>
		/// <returns>A list of word wrapped lines.</returns>
		/// <remarks>
		/// <para>
		/// An empty <c>text</c> string will result in one empty line.
		/// </para>
		/// <para>
		/// If <c>width</c> is 0, a single, empty line will be returned.
		/// </para>
		/// <para>
		/// If <c>width</c> is int.MaxValue, the text will be formatted to the maximum width possible. 
		/// </para>
		/// </remarks>
		public static List<ustring> Format (ustring text, int width, TextAlignment talign, bool wordWrap, bool preserveTrailingSpaces = false)
		{
			if (width < 0) {
				throw new ArgumentOutOfRangeException ("width cannot be negative");
			}
			if (preserveTrailingSpaces && !wordWrap) {
				throw new ArgumentException ("if 'preserveTrailingSpaces' is true, then 'wordWrap' must be true either.");
			}
			List<ustring> lineResult = new List<ustring> ();

			if (ustring.IsNullOrEmpty (text) || width == 0) {
				lineResult.Add (ustring.Empty);
				return lineResult;
			}

			if (wordWrap == false) {
				text = ReplaceCRLFWithSpace (text);
				lineResult.Add (ClipAndJustify (text, width, talign));
				return lineResult;
			}

			var runes = text.ToRuneList ();
			int runeCount = runes.Count;
			int lp = 0;
			for (int i = 0; i < runeCount; i++) {
				Rune c = runes [i];
				if (c == '\n') {
					var wrappedLines = WordWrap (ustring.Make (runes.GetRange (lp, i - lp)), width, preserveTrailingSpaces);
					foreach (var line in wrappedLines) {
						lineResult.Add (ClipAndJustify (line, width, talign));
					}
					if (wrappedLines.Count == 0) {
						lineResult.Add (ustring.Empty);
					}
					lp = i + 1;
				}
			}
			foreach (var line in WordWrap (ustring.Make (runes.GetRange (lp, runeCount - lp)), width, preserveTrailingSpaces)) {
				lineResult.Add (ClipAndJustify (line, width, talign));
			}

			return lineResult;
		}

		/// <summary>
		/// Computes the number of lines needed to render the specified text given the width.
		/// </summary>
		/// <returns>Number of lines.</returns>
		/// <param name="text">Text, may contain newlines.</param>
		/// <param name="width">The minimum width for the text.</param>
		public static int MaxLines (ustring text, int width)
		{
			var result = TextFormatter.Format (text, width, TextAlignment.Left, true);
			return result.Count;
		}

		/// <summary>
		/// Computes the maximum width needed to render the text (single line or multiple lines) given a minimum width.
		/// </summary>
		/// <returns>Max width of lines.</returns>
		/// <param name="text">Text, may contain newlines.</param>
		/// <param name="width">The minimum width for the text.</param>
		public static int MaxWidth (ustring text, int width)
		{
			var result = TextFormatter.Format (text, width, TextAlignment.Left, true);
			var max = 0;
			result.ForEach (s => {
				var m = 0;
				s.ToRuneList ().ForEach (r => m += Rune.ColumnWidth (r));
				if (m > max) {
					max = m;
				}
			});
			return max;
		}

		/// <summary>
		///  Calculates the rectangle required to hold text, assuming no word wrapping.
		/// </summary>
		/// <param name="x">The x location of the rectangle</param>
		/// <param name="y">The y location of the rectangle</param>
		/// <param name="text">The text to measure</param>
		/// <returns></returns>
		public static Rect CalcRect (int x, int y, ustring text)
		{
			if (ustring.IsNullOrEmpty (text)) {
				return new Rect (new Point (x, y), Size.Empty);
			}

			int mw = 0;
			int ml = 1;

			int cols = 0;
			foreach (var rune in text) {
				if (rune == '\n') {
					ml++;
					if (cols > mw) {
						mw = cols;
					}
					cols = 0;
				} else {
					if (rune != '\r') {
						cols++;
						var rw = Rune.ColumnWidth (rune);
						if (rw > 0) {
							rw--;
						}
						cols += rw;
					}
				}
			}
			if (cols > mw) {
				mw = cols;
			}

			return new Rect (x, y, mw, ml);
		}

		/// <summary>
		/// Finds the hotkey and its location in text. 
		/// </summary>
		/// <param name="text">The text to look in.</param>
		/// <param name="hotKeySpecifier">The hotkey specifier (e.g. '_') to look for.</param>
		/// <param name="firstUpperCase">If <c>true</c> the legacy behavior of identifying the first upper case character as the hotkey will be enabled.
		/// Regardless of the value of this parameter, <c>hotKeySpecifier</c> takes precedence.</param>
		/// <param name="hotPos">Outputs the Rune index into <c>text</c>.</param>
		/// <param name="hotKey">Outputs the hotKey.</param>
		/// <returns><c>true</c> if a hotkey was found; <c>false</c> otherwise.</returns>
		public static bool FindHotKey (ustring text, Rune hotKeySpecifier, bool firstUpperCase, out int hotPos, out Key hotKey)
		{
			if (ustring.IsNullOrEmpty (text) || hotKeySpecifier == (Rune)0xFFFF) {
				hotPos = -1;
				hotKey = Key.Unknown;
				return false;
			}

			Rune hot_key = (Rune)0;
			int hot_pos = -1;

			// Use first hot_key char passed into 'hotKey'.
			// TODO: Ignore hot_key of two are provided
			// TODO: Do not support non-alphanumeric chars that can't be typed
			int i = 0;
			foreach (Rune c in text) {
				if ((char)c != 0xFFFD) {
					if (c == hotKeySpecifier) {
						hot_pos = i;
					} else if (hot_pos > -1) {
						hot_key = c;
						break;
					}
				}
				i++;
			}


			// Legacy support - use first upper case char if the specifier was not found
			if (hot_pos == -1 && firstUpperCase) {
				i = 0;
				foreach (Rune c in text) {
					if ((char)c != 0xFFFD) {
						if (Rune.IsUpper (c)) {
							hot_key = c;
							hot_pos = i;
							break;
						}
					}
					i++;
				}
			}

			if (hot_key != (Rune)0 && hot_pos != -1) {
				hotPos = hot_pos;

				if (hot_key.IsValid && char.IsLetterOrDigit ((char)hot_key)) {
					hotKey = (Key)char.ToUpperInvariant ((char)hot_key);
					return true;
				}
			}

			hotPos = -1;
			hotKey = Key.Unknown;
			return false;
		}

		/// <summary>
		/// Replaces the Rune at the index specified by the <c>hotPos</c> parameter with a tag identifying 
		/// it as the hotkey.
		/// </summary>
		/// <param name="text">The text to tag the hotkey in.</param>
		/// <param name="hotPos">The Rune index of the hotkey in <c>text</c>.</param>
		/// <returns>The text with the hotkey tagged.</returns>
		/// <remarks>
		/// The returned string will not render correctly without first un-doing the tag. To undo the tag, search for 
		/// Runes with a bitmask of <c>otKeyTagMask</c> and remove that bitmask.
		/// </remarks>
		public ustring ReplaceHotKeyWithTag (ustring text, int hotPos)
		{
			// Set the high bit
			var runes = text.ToRuneList ();
			if (Rune.IsLetterOrNumber (runes [hotPos])) {
				runes [hotPos] = new Rune ((uint)runes [hotPos] | HotKeyTagMask);
			}
			return ustring.Make (runes);
		}

		/// <summary>
		/// Removes the hotkey specifier from text.
		/// </summary>
		/// <param name="text">The text to manipulate.</param>
		/// <param name="hotKeySpecifier">The hot-key specifier (e.g. '_') to look for.</param>
		/// <param name="hotPos">Returns the position of the hot-key in the text. -1 if not found.</param>
		/// <returns>The input text with the hotkey specifier ('_') removed.</returns>
		public static ustring RemoveHotKeySpecifier (ustring text, int hotPos, Rune hotKeySpecifier)
		{
			if (ustring.IsNullOrEmpty (text)) {
				return text;
			}

			// Scan 
			ustring start = ustring.Empty;
			int i = 0;
			foreach (Rune c in text) {
				if (c == hotKeySpecifier && i == hotPos) {
					i++;
					continue;
				}
				start += ustring.Make (c);
				i++;
			}
			return start;
		}

		/// <summary>
		/// Draws the text held by <see cref="TextFormatter"/> to <see cref="Application.Driver"/> using the colors specified.
		/// </summary>
		/// <param name="bounds">Specifies the screen-relative location and maximum size for drawing the text.</param>
		/// <param name="normalColor">The color to use for all text except the hotkey</param>
		/// <param name="hotColor">The color to use to draw the hotkey</param>
		public void Draw (Rect bounds, Attribute normalColor, Attribute hotColor)
		{
			// With this check, we protect against subclasses with overrides of Text (like Button)
			if (ustring.IsNullOrEmpty (text)) {
				return;
			}

			Application.Driver?.SetAttribute (normalColor);

			// Use "Lines" to ensure a Format (don't use "lines"))
			for (int line = 0; line < Lines.Count; line++) {
				if (line > bounds.Height)
					continue;
				var runes = lines [line].ToRunes ();
				int x;
				switch (textAlignment) {
				case TextAlignment.Left:
				case TextAlignment.Justified:
					x = bounds.Left;
					CursorPosition = hotKeyPos;
					break;
				case TextAlignment.Right:
					x = bounds.Right - runes.Length;
					CursorPosition = bounds.Width - runes.Length + hotKeyPos;
					break;
				case TextAlignment.Centered:
					x = bounds.Left + (bounds.Width - runes.Length) / 2;
					CursorPosition = (bounds.Width - runes.Length) / 2 + hotKeyPos;
					break;
				default:
					throw new ArgumentOutOfRangeException ();
				}
				var col = bounds.Left;
				for (var idx = bounds.Left; idx < bounds.Left + bounds.Width; idx++) {
					Application.Driver?.Move (col, bounds.Top + line);
					var rune = (Rune)' ';
					if (idx >= x && idx < (x + runes.Length)) {
						rune = runes [idx - x];
					}
					if ((rune & HotKeyTagMask) == HotKeyTagMask) {
						if (textAlignment == TextAlignment.Justified) {
							CursorPosition = idx - bounds.Left;
						}
						Application.Driver?.SetAttribute (hotColor);
						Application.Driver?.AddRune ((Rune)((uint)rune & ~HotKeyTagMask));
						Application.Driver?.SetAttribute (normalColor);
					} else {
						Application.Driver?.AddRune (rune);
					}
					col += Rune.ColumnWidth (rune);
					if (idx + 1 < runes.Length && col + Rune.ColumnWidth (runes [idx + 1]) > bounds.Width) {
						break;
					}
				}
			}
		}
	}
}
