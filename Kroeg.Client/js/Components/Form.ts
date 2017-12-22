import { EntityStore, StoreActivityToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";

type FakeASObject = {[a: string]: string|string[]|boolean|FakeASObject[]};

export class Form implements IComponent {
    private sendTo: string;

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        this.sendTo = element.dataset.endpoint;
        element.addEventListener("submit", (e) => this.submit(e));
    }

    static find(elem: HTMLElement): HTMLFormElement {
        if (elem.parentElement == null) return null;
        if (elem.nodeName == "FORM") return elem as HTMLFormElement;
        return Form.find(elem.parentElement);
    }

    private submit(e: Event) {
        e.preventDefault();

        let formData = new FormData(this.element as HTMLFormElement);
        let resultJson: FakeASObject = {"@context": ["https://www.w3.org/ns/activitystreams", window.location.origin + "/render/context"]};
        for (let item of (formData as any).keys()) {
            let value: string|string[]|boolean|FakeASObject[] = formData.getAll(item) as string[];
            if (item.endsWith(".bool")) {
                value = formData.get(item) == "true";
                item = item.substr(0, item.length - 5);
            }
            if (item.endsWith(".bool?")) {
                value = formData.get(item) == "true";
                item = item.substr(0, item.length - 6);
                if (!value) continue;
            }
            if (item.endsWith("$")) {
                let result: FakeASObject[] = [];
                for (let item of value as string[]) {
                    result.push(JSON.parse(item));
                }

                item = item.substr(0, item.length - 1);
                value = result;
            }

            if (Array.isArray(value)) {
                resultJson[item] = [];
                for (let it of (value as string[]))
                    if (it.length > 0)
                        (resultJson[item] as string[]).push(it);
            } else resultJson[item] = value;
        }

        let headers = new Headers();
        headers.append("Content-Type", 'application/ld+json; profile="https://www.w3.org/ns/activitystreams"');

        this.entityStore.session.authFetch(this.sendTo, {
            body: JSON.stringify(resultJson),
            method: 'POST',
            headers
        }).then(async (a) => {
            if (a.status == 201) {
                window.history.pushState({}, document.title, a.headers.get("Location"));
                window.dispatchEvent(new PopStateEvent('popstate', {state: {}}));
            } else {
                let data = await a.text();
                console.log(data);
                alert(data);
            }
        })
        return false;
    }

    unbind() {

    }
}