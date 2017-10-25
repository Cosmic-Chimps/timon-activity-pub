import { EntityStore } from "./EntityStore";
import * as AS from "./AS";
import { Session } from './Session';

export enum TemplateItemType {
    Element = 0,
    Text,
    Script
}

export class TemplateItem {
    public type: TemplateItemType;
    public data: string;
    public children: TemplateItem[];
    public arguments: {[name: string]: TemplateItem[]};
    public builder: { (regs: Registers): any };
}

export class TemplateService {
    private _templatePromise: Promise<{[item: string]: TemplateItem}>

    constructor() {
        this._templatePromise = this._getTemplates();
    }

    private async _getTemplates(): Promise<{[item: string]: TemplateItem}> {
        const result = await fetch("/settings/templates");
        return await result.json();
    }

    public getTemplates(): Promise<{[item: string]: TemplateItem}> {
        return this._templatePromise;
    }
}

export class RenderResult {
    public result: HTMLElement;
    public subRender: {id: string, into: HTMLElement, template: string}[];
    public componentHandles: HTMLElement[];
}

class _ASHandler {
    constructor(private _regs: Registers) {}

    public get(name: string): any[] {
        return AS.get(this._regs.object, name);
    }

    public take(name: string, def?: any) {
        if (def === undefined)
            def = "";
        if (!AS.has(this._regs.object, name)) return def;
        let item = this.get(name)[0];
        if (item === null) return def;
        return item;
    }

    public has(name: string) {
        return AS.has(this._regs.object, name);
    }

    public contains(name: string, val: any) {
        return AS.contains(this._regs.object, name, val);
    }

    public containsAny(name: string, val: any[]) {
        return AS.containsAny(this._regs.object, name, val);
    }
}

class RendererInfo {
    constructor(private session: Session) {}

    public get client() { return true; }
    public get server() { return false; }

    public get loggedInAs() { return this.session.user; }

    private static _allowed_attributes = /^(href|rel|class)$/;
    private static _allowed_classes = /^((h|p|u|dt|e)-.*|mention|hashtag|ellipsis|invisible)$/;
    private static _disallowed_nodes = /^(script|object|embed)$/;

    public sanitize(data: string) {
        return data.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    private _clean(data: HTMLElement) {
        if (data.nodeType == document.TEXT_NODE) return;
        if (data.nodeType != document.ELEMENT_NODE) return;
        if (RendererInfo._disallowed_nodes.exec(data.nodeName)) {
            data.remove();
            return;
        }
        for (let attribute of Array.from(data.attributes)) {
            if (!RendererInfo._allowed_attributes.exec(attribute.name)) data.attributes.removeNamedItem(attribute.name);
        }

        for (let cl of Array.from(data.classList)) {
            if (!RendererInfo._allowed_classes.exec(cl)) data.classList.remove(cl);
        }

        for (let child of Array.from(data.childNodes)) {
            this._clean(child as HTMLElement);
        }
    }

    public clean(data: string) {
        let doc = document.createElement("div");
        doc.insertAdjacentHTML('beforeend', data);
        this._clean(doc);
        return doc.innerHTML;
    }
}

class Registers {
    public AS: _ASHandler;
    public Renderer: RendererInfo;
    public object: AS.ASObject;
    public item: any;
}

export class TemplateRenderer {
    private templates: {[item: string]: TemplateItem};

    public constructor(private templateService: TemplateService, private entityStore: EntityStore) {
    }

    public async prepare() {
        this.templates = await this.templateService.getTemplates();

        for (let templateName in this.templates)
            TemplateRenderer._buildDelegates(this.templates[templateName]);
    }

    private static _buildDelegates(item: TemplateItem) {
        if (item.type == TemplateItemType.Element)
        {
            for (let i in item.arguments)
                for (let subItem of item.arguments[i])
                    TemplateRenderer._buildDelegates(subItem);
            for (let subItem of item.children)
                TemplateRenderer._buildDelegates(subItem);
        }
        else if (item.type == TemplateItemType.Script)
        {
            item.builder = new Function("regs", `let AS = regs.AS; let Renderer = regs.Renderer; let object = regs.object; let item=regs.item; return (${item.data});`) as (regs: Registers) => any;
        }
    }

    private _parse(item: TemplateItem, data: AS.ASObject, regs: Registers, override?: boolean): any {
        if (!override && item.type == TemplateItemType.Text) return item.data;

        regs.object = data || {};
        return item.builder(regs);
    }

    private _parseArr(item: TemplateItem[], data: AS.ASObject, regs: Registers): string {
        let result = "";
        for (let it of item)
            result += this._parse(it, data, regs, false);

        return result;
    }

    private _render(item: TemplateItem, data: AS.ASObject, regs: Registers, renderResult: RenderResult, render: boolean, element?: HTMLElement): HTMLElement {
        if ("x-render-if" in item.arguments && render) {
            render = this._parse(item.arguments["x-render-if"][0], data, regs, true) as boolean;
        }
        if ("x-render" in item.arguments && render)
        {
            let itemId = data.id;
            let templateName = item.arguments["x-render"][0].data;
            let template = this.templates[templateName];
            let renderId = itemId;
            if ("x-render-id" in item.arguments)
                renderId = this._parse(item.arguments["x-render-id"][0], data, regs, true) as string;
            if (renderId != itemId)
            {
                let rendered = this._render(template, null, regs, renderResult, false);
                rendered.dataset["render"] = templateName;
                rendered.dataset["id"] = renderId;
                renderResult.subRender.push({id: renderId, into: rendered, template: templateName});
                return rendered;
            }

            item = template;
        }
        if (element === undefined)
            element = document.createElement(item.data);
        else
            while (element.firstChild) element.removeChild(element.firstChild);

        for (let arg in item.arguments) {
            if (!arg.startsWith("x-"))
                element.setAttribute(arg, this._parseArr(item.arguments[arg], data, regs));
        }

        if (!render) return element;

        if ("data-component" in item.arguments) {
            renderResult.componentHandles.push(element);
        }

        if (render) {
            let text = "";
            for (let content of item.children) {
                if (content.type == TemplateItemType.Text || content.type == TemplateItemType.Script) {
                    if (content.type == TemplateItemType.Text)
                        text += content.data;
                    else if (content.type == TemplateItemType.Script)
                        text += this._parse(content, data, regs, false);
                }
                else
                {
                    if (text.length > 0) {
                        element.insertAdjacentHTML("beforeend", text);
                        text = "";
                    }
                    if ("x-for-in" in content.arguments) {
                        let resultItems = this._parse(content.arguments["x-for-in"][0], data, regs, true) as any[];
                        let prevItem = regs.item;
                        for (let subItem of resultItems) {
                            regs.item = subItem;
                            element.appendChild(this._render(content, data, regs, renderResult, true));
                        }
                        regs.item = prevItem;

                        continue;
                    } else if ("x-if" in content.arguments) {
                        let result = this._parse(content.arguments["x-if"][0], data, regs, true) as boolean;
                        if (!result) continue;
                    } else if ("x-else" in content.arguments) {
                        let result = this._parse(content.arguments["x-else"][0], data, regs, true) as boolean;
                        if (result) continue;
                    }

                    element.appendChild(this._render(content, data, regs, renderResult, true));
                }
            }

            if (text.length > 0) {
                element.insertAdjacentHTML("beforeend", text);
                text = "";
            }
        }

        return element;
    }

    public render(template: string, data: AS.ASObject, elem?: HTMLElement): RenderResult {
        let regs = new Registers();
        regs.AS = new _ASHandler(regs);
        regs.Renderer = new RendererInfo(this.entityStore.session);

        let renderResult = new RenderResult();
        renderResult.subRender = [];
        renderResult.componentHandles = [];
        let result = this._render(this.templates[template], data, regs, renderResult, true, elem);
        renderResult.result = result;

        return renderResult;
    }
}
