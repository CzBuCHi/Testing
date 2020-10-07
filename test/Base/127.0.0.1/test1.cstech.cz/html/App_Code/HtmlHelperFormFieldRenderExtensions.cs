using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Web;
using System.Web.WebPages;
using ASP;
using EasyWeb.WebSite.Razor;
using JetBrains.Annotations;

public static class HtmlHelperFormFieldRenderExtensions
{

    [NotNull]
    public delegate HelperResult FieldLayout([NotNull] Func<string> fieldName, [NotNull] Func<IHtmlString> label, [NotNull] Func<IHtmlString> field, [NotNull] Func<IHtmlString> validator);

    [NotNull]
    public delegate HelperResult ReadOnlyLayout([NotNull] Func<string> fieldName, [NotNull] Func<IHtmlString> label, [NotNull] Func<IHtmlString> value);

    [NotNull]
    public delegate HelperResult ButtonLayout([NotNull] Func<IHtmlString> field);


    [NotNull]
    public static HelperResult RenderCheckBoxLayout<TData, TProperty>([NotNull] this HtmlHelper htmlHelper, [NotNull] TData modelData, [NotNull] Expression<Func<TData, TProperty>> expression, FieldLayout layout = null, string explicitLabelText = null, object labelHtmlAttributes = null, ValidationAttributes labelValidationAttributes = null, object fieldHtmlAttributes = null, ValidationAttributes fieldValidationAttributes = null, object validationHtmlAttributes = null) where TData : class
    {
        var closure = new { htmlHelper, modelData, expression, explicitLabelText, labelHtmlAttributes, labelValidationAttributes, fieldHtmlAttributes, fieldValidationAttributes, validationHtmlAttributes };
        return (layout ?? DefaultLayouts.CheckBox)(
            () => htmlHelper.FieldNameFor(closure.expression, closure.modelData),
            () => htmlHelper.FieldLabelFor(closure.expression, closure.modelData, closure.explicitLabelText, closure.labelHtmlAttributes, closure.labelValidationAttributes),
            () => htmlHelper.FieldCheckBoxFor(closure.expression, closure.modelData, closure.fieldHtmlAttributes, closure.fieldValidationAttributes),
            () => htmlHelper.FieldValidationMessageFor(closure.expression, closure.modelData, closure.validationHtmlAttributes)
       );
    }

    [NotNull]
    public static HelperResult RenderHiddenBoxLayout<TData, TProperty>([NotNull] this HtmlHelper htmlHelper, [NotNull] TData modelData, [NotNull] Expression<Func<TData, TProperty>> expression, object htmlAttributes = null, bool isReadOnly = false) where TData : class
    {
        return new HelperResult(writer => writer.Write(htmlHelper.FieldHiddenFor(expression, modelData, htmlAttributes)));
    }

    [NotNull]
    public static HelperResult RenderPasswordLayout<TData, TProperty>([NotNull] this HtmlHelper htmlHelper, [NotNull] TData modelData, [NotNull] Expression<Func<TData, TProperty>> expression, FieldLayout layout = null, string explicitLabelText = null, object labelHtmlAttributes = null, ValidationAttributes labelValidationAttributes = null, object fieldHtmlAttributes = null, ValidationAttributes fieldValidationAttributes = null, object validationHtmlAttributes = null) where TData : class
    {
        var closure = new { htmlHelper, modelData, expression, explicitLabelText, labelHtmlAttributes, labelValidationAttributes, fieldHtmlAttributes, fieldValidationAttributes, validationHtmlAttributes };
        return (layout ?? DefaultLayouts.Password)(
                () => htmlHelper.FieldNameFor(closure.expression, closure.modelData),
                () => htmlHelper.FieldLabelFor(closure.expression, closure.modelData, closure.explicitLabelText, closure.labelHtmlAttributes, closure.labelValidationAttributes),
                () => htmlHelper.FieldPasswordFor(closure.expression, closure.modelData, closure.fieldHtmlAttributes, closure.fieldValidationAttributes),
                () => htmlHelper.FieldValidationMessageFor(closure.expression, closure.modelData, closure.validationHtmlAttributes)
            );
    }
    [NotNull]
    public static HelperResult RenderTextAreaLayout<TData, TProperty>([NotNull] this HtmlHelper htmlHelper, [NotNull] TData modelData, [NotNull] Expression<Func<TData, TProperty>> expression, FieldLayout layout = null, string explicitLabelText = null, object labelHtmlAttributes = null, ValidationAttributes labelValidationAttributes = null, object fieldHtmlAttributes = null, ValidationAttributes fieldValidationAttributes = null, object validationHtmlAttributes = null) where TData : class
    {
        var closure = new { htmlHelper, modelData, expression, explicitLabelText, labelHtmlAttributes, labelValidationAttributes, fieldHtmlAttributes, fieldValidationAttributes, validationHtmlAttributes };
        return (layout ?? DefaultLayouts.TextArea)(
            () => htmlHelper.FieldNameFor(closure.expression, closure.modelData),
            () => htmlHelper.FieldLabelFor(closure.expression, closure.modelData, closure.explicitLabelText, closure.labelHtmlAttributes, closure.labelValidationAttributes),
            () => htmlHelper.FieldTextAreaFor(closure.expression, closure.modelData, closure.fieldHtmlAttributes, closure.fieldValidationAttributes),
            () => htmlHelper.FieldValidationMessageFor(closure.expression, closure.modelData, closure.validationHtmlAttributes)
       );
    }

    [NotNull]
    public static HelperResult RenderTextBoxLayout<TData, TProperty>([NotNull] this HtmlHelper htmlHelper, [NotNull] TData modelData, [NotNull] Expression<Func<TData, TProperty>> expression, FieldLayout layout = null, string explicitLabelText = null, object labelHtmlAttributes = null, ValidationAttributes labelValidationAttributes = null, object fieldHtmlAttributes = null, ValidationAttributes fieldValidationAttributes = null, object validationHtmlAttributes = null) where TData : class
    {
        var closure = new { htmlHelper, modelData, expression, explicitLabelText, labelHtmlAttributes, labelValidationAttributes, fieldHtmlAttributes, fieldValidationAttributes, validationHtmlAttributes };
        return (layout ?? DefaultLayouts.TextBox)(
            () => htmlHelper.FieldNameFor(closure.expression, closure.modelData),
            () => htmlHelper.FieldLabelFor(closure.expression, closure.modelData, closure.explicitLabelText, closure.labelHtmlAttributes, closure.labelValidationAttributes),
            () => htmlHelper.FieldTextBoxFor(closure.expression, closure.modelData, closure.fieldHtmlAttributes, closure.fieldValidationAttributes),
            () => htmlHelper.FieldValidationMessageFor(closure.expression, closure.modelData, closure.validationHtmlAttributes)
       );
    }

    [NotNull]
    public static HelperResult RenderReadOnlyLayout<TData, TProperty>([NotNull] this HtmlHelper htmlHelper, [NotNull] TData modelData, [NotNull] Expression<Func<TData, TProperty>> expression, ReadOnlyLayout layout = null, string explicitLabelText = null, object labelHtmlAttributes = null, object fieldHtmlAttributes = null) where TData : class
    {
        var closure = new { htmlHelper, modelData, expression, explicitLabelText, labelHtmlAttributes, fieldHtmlAttributes };
        return (layout ?? DefaultLayouts.ReadOnly)(
            () => htmlHelper.FieldNameFor(closure.expression, closure.modelData),
            () => htmlHelper.FieldLabelFor(closure.expression, closure.modelData, closure.explicitLabelText, closure.labelHtmlAttributes),
            () => new HtmlString(string.Format("<span data-field='{0}'>{1}</span>", htmlHelper.FieldNameFor(closure.expression, closure.modelData), htmlHelper.FieldValueFor(closure.expression, closure.modelData)))
        );
    }

    [NotNull]
    public static HelperResult RenderButtonLayout([NotNull] this HtmlHelper htmlHelper, string text = null, ButtonLayout layout = null, FormButtonType buttonType = FormButtonType.Submit, FormButtonRenderType renderType = FormButtonRenderType.Input, object htmlAttributes = null)
    {
        return new HelperResult(writer => writer.Write((layout ?? DefaultLayouts.Button)(() => htmlHelper.FormButton(text, buttonType, renderType, htmlAttributes))));
    }
}