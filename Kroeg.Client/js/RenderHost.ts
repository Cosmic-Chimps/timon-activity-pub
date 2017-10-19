import { TemplateRenderer, TemplateService, RenderResult } from "./TemplateService";
import { EntityStore, StoreActivityToken } from "./EntityStore";
import { ASObject } from "./AS";

export class RenderHost {
    private _lastResult: RenderResult;
    private _object: ASObject;
    private _id: string;
    private _template: string;
    private _dom: HTMLElement;
    private _storeActivityToken: StoreActivityToken;
    private _subrender: RenderHost[] = [];

    public get id(): string { return this._id; }
    public set id(value: string) { this._id = value; this.update(true); }

    public get template(): string { return this._template; }
    public set template(value: string) { this._template = value; this.render(); }

    constructor(private renderer: TemplateRenderer, private store: EntityStore, id: string, template: string, dom?: HTMLElement, private _parent?: RenderHost) {
        this._dom = dom != null ? dom : document.createElement("div");
        this._id = id;
        this._template = template;
        this.update(true);
    }

    public async update(reload: boolean = false) {
        if (this._storeActivityToken != null)
            this.store.deregister(this._storeActivityToken);

        if (reload) this._dom.innerHTML = '<span class="renderhost_loading">🍺</span>';        

        const handlers: {[name: string]: (oldValue: ASObject, newValue: ASObject) => void} = {};
        handlers[this._id] = this._reupdate.bind(this);
        this._storeActivityToken = this.store.register(handlers);

        this._object = await this.store.get(this._id);
        this.render();
    }

    public deregister() {
        if (this._storeActivityToken != null)
            this.store.deregister(this._storeActivityToken);
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
        this._subrender.splice(0);

        const result = await this.renderer.render(this._template, this._object, this._dom);

        for (let old of this._subrender)
            old.deregister();

        this._subrender = [];

        for (let item of result.subRender) {
            let host = new RenderHost(this.renderer, this.store, item.id, item.template, item.into, this);
            this._subrender.push(host);
        }
    }
}
