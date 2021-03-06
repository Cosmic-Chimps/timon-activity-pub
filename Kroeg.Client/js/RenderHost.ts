import { TemplateRenderer, TemplateService, RenderResult, TemplateItem } from "./TemplateService";
import { EntityStore, StoreActivityToken } from "./EntityStore";
import { ASObject } from "./AS";
import { IComponent, IComponentType } from "./IComponent";

import * as UserPicker from "./Components/UserPicker";
import * as Wysiwyg from "./Components/Wysiwyg";
import * as Form from "./Components/Form";
import * as SubRenderhost from "./Components/SubRenderhost";
import * as Emoji from "./Components/Emoji";
import * as Live from "./Components/Live";

export class RenderHost {
    private _lastResult: RenderResult;
    private _object: ASObject;
    private _id: string;
    private _template: string|TemplateItem;
    private _dom: HTMLElement;
    private _storeActivityToken: StoreActivityToken;
    private _subrender: RenderHost[] = [];
    private _components: IComponent[] = [];

    private static _components: {[type: string]: IComponentType } = {
        userpicker: UserPicker.UserPicker,
        wysiwyg: Wysiwyg.Wysiwyg,
        form: Form.Form,
        renderhost: SubRenderhost.SubRenderhost,
        emoji: Emoji.Emoji,
        live: Live.Live
    };

    public static registerComponent(name: string, type: IComponentType) {
        RenderHost._components[name] = type;
    }

    public get id(): string { return this._id; }
    public set id(value: string) { this._id = value; this.update(true); }

    public get data(): {[name: string]: string} { return this._renderData; }
    public set data(value: {[name: string]: string}) { this._renderData = value; }

    public get template(): string|TemplateItem { return this._template; }
    public set template(value: string|TemplateItem) { this._template = value; this.render(); }

    public depth: number = 0;

    constructor(public renderer: TemplateRenderer, private store: EntityStore, id: string, template: string|TemplateItem, dom?: HTMLElement, private _parent?: RenderHost, private _renderData?: {[name: string]: string}, private _parentAS?: ASObject) {
        this._dom = dom != null ? dom : document.createElement("div");
        this._id = id;
        this._template = template;
        this.depth = _parent != null ? (_parent.depth + 1) : 0;
        this.update(true);
    }

    public async update(reload: boolean = false) {
        if (this.depth > 40) return;
        if (this._storeActivityToken != null)
            this.store.deregister(this._storeActivityToken);

        if (reload) this._dom.innerHTML = '<span class="renderhost_loading">????</span> ' + this._id;

        const handlers: {[name: string]: (oldValue: ASObject, newValue: ASObject) => void} = {};
        handlers[this._id] = this._reupdate.bind(this);
        this._storeActivityToken = this.store.register(handlers);

        this._object = await this.store.get(this._id);
        this.render();
    }

    public deregister() {
        if (this._storeActivityToken != null)
            this.store.deregister(this._storeActivityToken);
        for (let data of this._components)
            data.unbind();
    }

    public get element(): Element { return this._dom; }

    private _reupdate(old: ASObject, newObject: ASObject) {
        this._object = newObject;
        this.render();
    }

    private static _counter = 0;

    public async render(): Promise<void> {
        if (this._object == null) return;

        for (let subrender of this._subrender)
            subrender.deregister();
        for (let data of this._components)
            data.unbind();
        this._subrender.splice(0);

        const result = await this.renderer.render(this._template, this._object, this._dom, this._renderData, this._parentAS);

        for (let old of this._subrender)
            old.deregister();

        this._subrender = [];
        this._components = [];

        for (let item of result.subRender) {
            let host = new RenderHost(this.renderer, this.store, item.id, item.template, item.into, this, item.data, item.parent);
            this._subrender.push(host);
        }

        for (let elem of result.componentHandles) {
            let type = elem.dataset["component"];
            if (type in RenderHost._components) {
                this._components.push(new (RenderHost._components[type])(this, this.store, elem));
            }
        }
    }
}
