using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Windows.Markup;
using System.Xml;
using System.IO;

namespace PoshConsole
{
   public static class Utilities
   {

      public static bool IsModifierOn(this KeyEventArgs e, ModifierKeys modifier)
      {
         return (e.KeyboardDevice.Modifiers & modifier) == modifier;
      }

   }

   public static class EnumHelper
   {

      #region [rgn] Methods (7)

      // [rgn] Public Methods (7)

      //public static IEnumerable<TOutput> ConvertAll<TInput, TOutput>( this IEnumerable<TInput> Input, Converter<TInput, TOutput> converter)
      //{
      //    if (Input != null)
      //    {
      //        foreach (TInput item in Input)
      //        {
      //            yield return converter(item);
      //        }
      //    }
      //}

      //public static T First<T>( this System.Collections.ObjectModel.Collection<T> collection)
      //{
      //    return (T)collection[0];
      //}

      //public static void ForEach<T>(IEnumerable<T> Input, Action<T> action)
      //{
      //    if (Input.fo != null)
      //    {
      //        foreach (T item in Input)
      //        {
      //            action(item);
      //        }
      //    }
      //}

      public static bool IsDefined<TEnum>(TEnum value) where TEnum : struct
      {
         return Enum.IsDefined(typeof(TEnum), value);
      }

      public static TEnum? Parse<TEnum>(string str) where TEnum : struct
      {
         return Parse<TEnum>(str, false);
      }

      public static TEnum? Parse<TEnum>(string str, bool ignoreCase) where TEnum : struct
      {
         TEnum value = (TEnum)Enum.Parse(typeof(TEnum), str, ignoreCase);
         if (IsDefined<TEnum>(value)) return value;
         return null;
      }

      public static void VerifyIsDefined<TEnum>(TEnum value, string argumentName) where TEnum : struct
      {
         if (!IsDefined<TEnum>(value))
         {
            throw new InvalidEnumArgumentException(argumentName, Convert.ToInt32(value), typeof(TEnum));
         }
      }

      #endregion [rgn]

   }

   public static class LinkedListStackQueue
   {
      public static T Dequeue<T>(this LinkedList<T> list)
      {
         T result = list.First.Value;
         list.RemoveFirst();
         return result;
      }

      public static T Pop<T>(this LinkedList<T> list)
      {
         T result = list.Last.Value;
         list.RemoveLast();
         return result;
      }

      public static void Enqueue<T>(this LinkedList<T> list, T item)
      {
         list.AddLast(item);
      }

      public static void Push<T>(this LinkedList<T> list, T item)
      {
         list.AddFirst(item);
      }
   }

   internal static class PipelineHelper
   {

      #region [rgn] Methods (2)

      // [rgn] Public Methods (2)

      public static bool IsDone(this System.Management.Automation.Runspaces.PipelineStateInfo psi)
      {
         return
             psi.State == System.Management.Automation.Runspaces.PipelineState.Completed ||
             psi.State == System.Management.Automation.Runspaces.PipelineState.Stopped ||
             psi.State == System.Management.Automation.Runspaces.PipelineState.Failed;
      }

      public static bool IsFailed(this System.Management.Automation.Runspaces.PipelineStateInfo info)
      {
         return info.State == System.Management.Automation.Runspaces.PipelineState.Failed;
      }

      #endregion [rgn]

   }


   internal static class RectHelper
   {

      #region [rgn] Methods (2)

      // [rgn] Public Methods (2)

      public static int Height(this System.Management.Automation.Host.Rectangle rect)
      {
         return rect.Bottom - rect.Top;
      }

      public static int Width(this System.Management.Automation.Host.Rectangle rect)
      {
         return rect.Right - rect.Left;
      }

      #endregion [rgn]

   }

   internal static class ThicknessHelper
   {

      #region [rgn] Methods (2)

      // [rgn] Public Methods (2)

      public static double Height(this System.Windows.Thickness t)
      {
         return t.Top + t.Bottom;
      }

      public static double Width(this System.Windows.Thickness t)
      {
         return t.Left + t.Right;
      }

      #endregion [rgn]

   }

}
