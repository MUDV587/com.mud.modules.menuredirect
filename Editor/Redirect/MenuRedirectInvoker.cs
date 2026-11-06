using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using L = MUD.Modules.MenuRedirect.MenuRedirectLocalization;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// 由生成的包装菜单调用：按原路径找到原始 [MenuItem] 方法并直接执行。
    ///
    /// 不能使用 <c>EditorApplication.ExecuteMenuItem(originalPath)</c>：
    /// 原菜单已经被 RemoveMenuItem 移除，ExecuteMenuItem 会返回 false，
    /// 表现为"新菜单能点，但什么也没发生"。
    /// </summary>
    public static class MenuRedirectInvoker
    {
        /// <summary>执行原菜单方法。返回 false 表示找不到方法或方法抛出异常。</summary>
        public static bool Execute(string originalPath)
        {
            string path = MenuRegistry.NormalizePath(originalPath);

            if (path.Length == 0)
                return false;

            MenuRegistry registry = MenuRedirectEngine.Registry;

            if (!registry.TryGetExecutable(path, out MenuRegistry.MenuRegistration? registration) ||
                registration == null)
            {
                Debug.LogError(L.F("invoker.noMethod", originalPath));
                return false;
            }

            if (!TryBuildArguments(registration.Method, out object[] arguments))
            {
                Debug.LogError(L.F(
                    "invoker.badSignature",
                    registration.TypeFullName,
                    registration.Method.Name,
                    originalPath));
                return false;
            }

            try
            {
                registration.Method.Invoke(null, arguments);
                return true;
            }
            catch (TargetInvocationException exception)
            {
                Debug.LogException(exception.InnerException ?? exception);
                return false;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        /// <summary>执行原菜单的 validate 方法；没有 validate 时返回 true。</summary>
        public static bool Validate(string originalPath)
        {
            string path = MenuRegistry.NormalizePath(originalPath);

            if (path.Length == 0)
                return true;

            MenuRegistry registry = MenuRedirectEngine.Registry;
            var validators = registry.GetValidateMethods(path);

            foreach (MenuRegistry.MenuRegistration registration in validators)
            {
                if (!TryBuildArguments(registration.Method, out object[] arguments))
                    continue;

                try
                {
                    object? result = registration.Method.Invoke(null, arguments);

                    // validate 方法返回 bool；返回 false 时对应菜单项置灰。
                    if (result is bool allowed && !allowed)
                        return false;
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogException(exception.InnerException ?? exception);
                    return false;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    return false;
                }
            }

            return true;
        }

        private static bool TryBuildArguments(MethodInfo method, out object[] arguments)
        {
            arguments = Array.Empty<object>();

            if (!method.IsStatic)
                return false;

            ParameterInfo[] parameters = method.GetParameters();

            if (parameters.Length == 0)
                return true;

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(MenuCommand))
            {
                arguments = new object[] { new MenuCommand(Selection.activeObject) };
                return true;
            }

            return false;
        }
    }
}
